using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Models.Npc;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Balancing;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;
using RimMind.ModelService.Security;
using CoreHttpErrorClassifier = RimMind.Application.Features.Llm.HttpErrorClassifier;

namespace RimMind.ModelService.Client
{
    public class ModelServiceClient : IAIClient
    {
        private const int MaxErrorBodyChars = 500;
        private const int DefaultOverallTimeoutSeconds = 180;

        private readonly Func<IReadOnlyList<ModelEndpointConfig>> _endpointsProvider;
        private readonly Func<BalancingStrategy> _strategyProvider;
        private readonly ILoadBalancer _loadBalancer;
        private readonly EndpointFailoverPolicy _failoverPolicy;
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _httpSend;
        private readonly Func<long> _clockMs;
        private volatile bool _disposed;

        public int OverallTimeoutSeconds { get; set; } = DefaultOverallTimeoutSeconds;

        public ModelServiceClient(
            Func<IReadOnlyList<ModelEndpointConfig>> endpointsProvider,
            Func<BalancingStrategy> strategyProvider,
            ILoadBalancer? loadBalancer = null)
            : this(endpointsProvider, strategyProvider, loadBalancer, null, null)
        {
        }

        internal ModelServiceClient(
            Func<IReadOnlyList<ModelEndpointConfig>> endpointsProvider,
            Func<BalancingStrategy> strategyProvider,
            ILoadBalancer? loadBalancer,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? httpSendOverride,
            Func<long>? clockMsOverride = null)
        {
            _endpointsProvider = endpointsProvider ?? throw new ArgumentNullException(nameof(endpointsProvider));
            _strategyProvider = strategyProvider ?? throw new ArgumentNullException(nameof(strategyProvider));
            _clockMs = clockMsOverride ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _failoverPolicy = (loadBalancer as LoadBalancer)?.FailoverPolicy
                ?? new EndpointFailoverPolicy(
                    clockMs: _clockMs,
                    defaultIsolationMs: ModelEndpointConfig.DefaultCooldownMs,
                    transientRateLimitIsolationMs: EndpointFailoverPolicy.DefaultTransientRateLimitIsolationMs,
                    permanentIsolationMs: EndpointFailoverPolicy.DefaultPermanentIsolationMs);
            _loadBalancer = loadBalancer ?? new LoadBalancer(_failoverPolicy);
            _httpSend = httpSendOverride ?? ((req, ct) => RimMindHttpClientPool.NoRedirect.SendAsync(req, ct));
        }

        private static string SanitizeAndTruncate(string? text, string? knownSecret, int maxChars = MaxErrorBodyChars)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return SecretRedactor.ForMessageWithSecret(text, knownSecret, maxChars);
        }

        private static bool IsTransientError(RimMindError error) =>
            error.Code == RimMindErrorCode.ClientTransientFailure ||
            error.Code == RimMindErrorCode.Timeout;

        public bool IsLocalEndpoint
        {
            get
            {
                var endpoints = GetEndpointsSafe();
                bool hasEnabled = false;
                for (int i = 0; i < endpoints.Length; i++)
                {
                    var ep = endpoints[i];
                    if (ep == null || !ep.isEnabled)
                        continue;
                    hasEnabled = true;
                    if (!ep.IsLoopbackAddress)
                        return false;
                }
                return hasEnabled;
            }
        }
        public bool IsConfigured() => GetEndpointsSafe().Any(e => e.isEnabled);
        public bool SupportsStreaming => false;
        public bool SupportsNpcServerState => false;

        private ModelEndpointConfig[] GetEndpointsSafe()
        {
            var source = _endpointsProvider();
            if (source == null) return Array.Empty<ModelEndpointConfig>();

            lock (source)
            {
                try
                {
                    return source.ToArray();
                }
                catch (InvalidOperationException)
                {
                    var list = new List<ModelEndpointConfig>();
                    for (int i = 0; i < source.Count; i++)
                    {
                        try
                        {
                            if (i < source.Count)
                            {
                                var item = source[i];
                                if (item != null) list.Add(item);
                            }
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            break;
                        }
                    }
                    return list.ToArray();
                }
            }
        }

        public Task<Result<LlmResponse, RimMindError>> SendAsync(LlmRequestEnvelope envelope)
        {
            return SendCoreAsync(envelope, envelope.Ct);
        }

        private async Task<Result<LlmResponse, RimMindError>> SendCoreAsync(
            LlmRequestEnvelope envelope,
            CancellationToken callerToken)
        {
            if (_disposed || callerToken.IsCancellationRequested)
            {
                return Result<LlmResponse, RimMindError>.Err(RimMindErrors.Cancelled());
            }

            var endpoints = GetEndpointsSafe();
            var enabledEndpoints = endpoints?.Where(e => e != null && e.isEnabled).ToList();
            if (enabledEndpoints == null || enabledEndpoints.Count == 0)
            {
                return Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.ClientNotConfigured("No model service endpoints configured."));
            }

            var strategy = _strategyProvider();
            int overallSec = Math.Max(5, OverallTimeoutSeconds);
            using var overallCts = new CancellationTokenSource(TimeSpan.FromSeconds(overallSec));
            using var overallLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken, overallCts.Token);
            CancellationToken overallToken = overallLinkedCts.Token;

            var triedEndpoints = new HashSet<string>();
            int maxAttempts = Math.Max(1, enabledEndpoints.Count);
            string lastErrorMessage = "";
            RimMindError? lastError = null;
            RimMindError? lastSecurityError = null;
            RimMindError? lastQuotaOrAuthError = null;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (callerToken.IsCancellationRequested)
                {
                    return Result<LlmResponse, RimMindError>.Err(RimMindErrors.Cancelled());
                }
                if (overallCts.IsCancellationRequested)
                {
                    return Result<LlmResponse, RimMindError>.Err(
                        RimMindErrors.Timeout($"ModelService request timed out after {overallSec}s. Last error: {lastErrorMessage}"));
                }

                long nowMs = _clockMs();
                var untried = enabledEndpoints.Where(e => !triedEndpoints.Contains(e.id)).ToList();
                if (untried.Count == 0) break;

                var node = _loadBalancer.SelectEndpoint(untried, strategy, nowMs);
                if (node == null) break;

                triedEndpoints.Add(node.id);

                var protocol = ModelProtocolRegistry.Resolve(node);
                if (protocol.Capabilities.RequiresLoopback && !node.IsLoopbackAddress)
                {
                    long failTick = _clockMs();
                    node.RecordFailure(failTick);
                    lastSecurityError = RimMindErrors.ClientPermanent(
                        "Security violation: Local subscription gateway must be on loopback (127.0.0.1)");
                    lastErrorMessage = lastSecurityError.Message;
                    continue;
                }

                var (result, isRateLimited) = await TrySendToNodeAsync(envelope, node, protocol, overallToken, callerToken).ConfigureAwait(false);
                if (result.IsOk)
                {
                    return result;
                }

                if (callerToken.IsCancellationRequested || result.Error.Code == RimMindErrorCode.Cancelled)
                {
                    return result.Error.Code == RimMindErrorCode.Cancelled
                        ? result
                        : Result<LlmResponse, RimMindError>.Err(RimMindErrors.Cancelled());
                }

                // Content-moderation and permanent client errors must NOT trip circuit breakers or fail over.
                if (result.Error.Code == RimMindErrorCode.ContentFiltered ||
                    result.Error.Code == RimMindErrorCode.ClientPermanentFailure)
                {
                    return result;
                }

                string nodeKey = LoadBalancer.BuildNodeKey(node);

                // Billing / invalid-key failures isolate THIS node immediately and try backup nodes.
                if (result.Error.Code == RimMindErrorCode.ClientQuotaOrAuthFailure)
                {
                    long failTick = _clockMs();
                    node.IsolateImmediately(failTick);
                    RecordFailoverFailure(node, nodeKey, result.Error, isRateLimited: false, nowMs: failTick);
                    lastQuotaOrAuthError = result.Error;
                    lastErrorMessage = result.Error.Message;
                    continue;
                }

                if (!IsTransientError(result.Error))
                {
                    return result;
                }

                long failureTimestampMs = _clockMs();
                node.RecordFailure(failureTimestampMs);
                if (isRateLimited || node.consecutiveFailures >= 3)
                {
                    RecordFailoverFailure(node, nodeKey, result.Error, isRateLimited: isRateLimited, nowMs: failureTimestampMs);
                }
                lastError = result.Error;
                lastErrorMessage = result.Error.Message;
            }

            if (callerToken.IsCancellationRequested)
            {
                return Result<LlmResponse, RimMindError>.Err(RimMindErrors.Cancelled());
            }

            if (lastError == null && lastQuotaOrAuthError != null)
            {
                return Result<LlmResponse, RimMindError>.Err(lastQuotaOrAuthError);
            }

            if (lastError == null && lastSecurityError != null)
            {
                return Result<LlmResponse, RimMindError>.Err(lastSecurityError);
            }

            if (lastError != null && lastError.Code == RimMindErrorCode.Timeout)
            {
                return Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.Timeout($"All candidate endpoints timed out. Last error: {lastErrorMessage}"));
            }

            return Result<LlmResponse, RimMindError>.Err(
                RimMindErrors.ClientTransient($"All candidate endpoints failed. Last error: {lastErrorMessage}"));
        }

        private void RecordFailoverFailure(
            ModelEndpointConfig node,
            string nodeKey,
            RimMindError error,
            bool isRateLimited,
            long nowMs)
        {
            if (_loadBalancer is LoadBalancer lb)
            {
                lb.ObserveNodeRecoveryBaseline(node);
            }
            _failoverPolicy.RecordFailure(nodeKey, error, isRateLimited: isRateLimited, nowMs: nowMs);
        }

        private async Task<(Result<LlmResponse, RimMindError> Result, bool IsRateLimited)> TrySendToNodeAsync(
            LlmRequestEnvelope envelope,
            ModelEndpointConfig node,
            IModelProtocol protocol,
            CancellationToken overallToken,
            CancellationToken callerToken)
        {
            int attemptTimeoutSec = Math.Max(5, node.timeoutSeconds);
            using var attemptCts = new CancellationTokenSource(TimeSpan.FromSeconds(attemptTimeoutSec));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(overallToken, attemptCts.Token);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                string modelOverride = !string.IsNullOrEmpty(node.modelName) ? node.modelName : protocol.DefaultModel;
                var payloadResult = protocol.BuildRequestJson(envelope, node, modelOverride);
                if (payloadResult.IsErr)
                {
                    return (Result<LlmResponse, RimMindError>.Err(payloadResult.Error), false);
                }

                Uri completionUri = protocol.ResolveCompletionUri(node);
                using var request = new HttpRequestMessage(HttpMethod.Post, completionUri);
                string? sessionKey = !string.IsNullOrEmpty(envelope.NpcId) ? envelope.NpcId : envelope.RequestId;
                protocol.ApplyHeaders(request, node, sessionKey);

                using var content = new StringContent(payloadResult.Value, Encoding.UTF8, "application/json");
                request.Content = content;

                using var response = await _httpSend(request, linkedCts.Token).ConfigureAwait(false);
                string responseBody = response.Content != null
                    ? await response.Content.ReadAsStringAsync().ConfigureAwait(false)
                    : string.Empty;
                stopwatch.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    int statusCode = (int)response.StatusCode;
                    string safeBody = SanitizeAndTruncate(responseBody, node.apiKey);
                    RimMindError classifiedError = protocol.ClassifyHttpError(statusCode, safeBody, node);
                    bool isRateLimited = CoreHttpErrorClassifier.IsTransientRateLimitOrQueueError(statusCode, safeBody);
                    return (Result<LlmResponse, RimMindError>.Err(classifiedError), isRateLimited);
                }

                var parseResult = protocol.ParseResponse(responseBody, envelope);
                if (parseResult.IsOk)
                {
                    node.RecordSuccess((int)stopwatch.ElapsedMilliseconds);
                    _failoverPolicy.RecordSuccess(LoadBalancer.BuildNodeKey(node));
                    if (_loadBalancer is LoadBalancer lb)
                    {
                        lb.ObserveNodeRecoveryBaseline(node);
                    }

                    var val = parseResult.Value;
                    // Like Core's OpenAIClient, responses are correlated by the envelope's RequestId;
                    // the provider's own completion id is only a fallback for id-less envelopes.
                    string reqId = !string.IsNullOrEmpty(envelope.RequestId) ? envelope.RequestId : val.RequestId;
                    val = val.With(requestId: reqId, state: AIRequestState.Completed);
                    parseResult = Result<LlmResponse, RimMindError>.Ok(val);
                }

                return (parseResult, false);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                if (callerToken.IsCancellationRequested)
                {
                    return (Result<LlmResponse, RimMindError>.Err(RimMindErrors.Cancelled()), false);
                }
                return (Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.Timeout($"Node '{node.name}' timed out after {attemptTimeoutSec}s.")), false);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return (Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.ClientTransient($"Failed to reach node '{node.name}': {SanitizeAndTruncate(ex.Message, node.apiKey)}", ex)), false);
            }
        }

        public async Task<Result<LlmResponse, RimMindError>> SendStreamAsync(
            LlmRequestEnvelope envelope,
            Action<LlmChunk> onChunk,
            CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested || envelope.Ct.IsCancellationRequested)
            {
                return Result<LlmResponse, RimMindError>.Err(RimMindErrors.Cancelled());
            }

            using var callerCts = (ct.CanBeCanceled && envelope.Ct.CanBeCanceled && ct != envelope.Ct)
                ? CancellationTokenSource.CreateLinkedTokenSource(ct, envelope.Ct)
                : null;
            CancellationToken effectiveCt = callerCts?.Token ?? (ct.CanBeCanceled ? ct : envelope.Ct);

            var result = await SendCoreAsync(envelope, effectiveCt).ConfigureAwait(false);
            if (result.IsOk && onChunk != null)
            {
                var response = result.Value;
                if (!string.IsNullOrEmpty(response.ReasoningContent))
                {
                    onChunk(new LlmChunk { DeltaReasoningContent = response.ReasoningContent });
                }

                if (!string.IsNullOrEmpty(response.Content) || !string.IsNullOrEmpty(response.ToolCallsJson))
                {
                    onChunk(new LlmChunk
                    {
                        DeltaContent = string.IsNullOrEmpty(response.Content) ? null : response.Content,
                        DeltaToolCallsJson = response.ToolCallsJson
                    });
                }

                if (response.PromptTokens > 0 || response.CompletionTokens > 0 || response.CachedTokens > 0)
                {
                    onChunk(new LlmChunk
                    {
                        DeltaPromptTokens = response.PromptTokens,
                        DeltaCompletionTokens = response.CompletionTokens,
                        DeltaCachedTokens = response.CachedTokens
                    });
                }

                onChunk(new LlmChunk
                {
                    IsLast = true,
                    FinalResponse = response
                });
            }

            return result;
        }

        public Task<Result<bool, RimMindError>> SpawnNpcAsync(NpcProfile profile) =>
            Task.FromResult(Result<bool, RimMindError>.Ok(true));

        public Task<Result<bool, RimMindError>> KillNpcAsync(string npcId) =>
            Task.FromResult(Result<bool, RimMindError>.Ok(true));

        public Task<Result<List<string>, RimMindError>> QueryNpcMemoriesAsync(string npcId, string query, int limit) =>
            Task.FromResult(Result<List<string>, RimMindError>.Ok(new List<string>()));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
