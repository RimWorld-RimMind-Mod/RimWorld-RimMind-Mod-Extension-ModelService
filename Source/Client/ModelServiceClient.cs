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
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Balancing;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;
using RimMind.ModelService.Security;

namespace RimMind.ModelService.Client
{
    public class ModelServiceClient : IAIClient
    {
        private static readonly HttpClient HttpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        private readonly Func<IReadOnlyList<ModelEndpointConfig>> _endpointsProvider;
        private readonly Func<BalancingStrategy> _strategyProvider;
        private readonly ILoadBalancer _loadBalancer;
        private bool _disposed;

        public ModelServiceClient(
            Func<IReadOnlyList<ModelEndpointConfig>> endpointsProvider,
            Func<BalancingStrategy> strategyProvider,
            ILoadBalancer? loadBalancer = null)
        {
            _endpointsProvider = endpointsProvider ?? throw new ArgumentNullException(nameof(endpointsProvider));
            _strategyProvider = strategyProvider ?? throw new ArgumentNullException(nameof(strategyProvider));
            _loadBalancer = loadBalancer ?? new LoadBalancer();
        }

        public bool IsLocalEndpoint => false;
        public bool IsConfigured() => _endpointsProvider().Any(e => e.isEnabled);
        public bool SupportsStreaming => false;
        public bool SupportsNpcServerState => false;

        public async Task<Result<LlmResponse, RimMindError>> SendAsync(LlmRequestEnvelope envelope)
        {
            var endpoints = _endpointsProvider()?.ToArray();
            if (endpoints == null || endpoints.Length == 0)
            {
                return Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.ClientNotConfigured("No model service endpoints configured."));
            }

            var strategy = _strategyProvider();
            long currentTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var triedEndpoints = new HashSet<string>();
            var eligible = _loadBalancer.GetEligibleEndpoints(endpoints, currentTick);
            // If all endpoints are temporarily isolated by circuit breaker, fall back to enabled endpoints
            // so LoadBalancer.SelectEndpoint can execute its self-healing probe branch!
            var candidatePool = eligible.Count > 0
                ? eligible
                : endpoints.Where(e => e.isEnabled).ToList();

            int maxAttempts = Math.Max(1, candidatePool.Count);
            string lastErrorMessage = "";

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var candidates = candidatePool.Where(e => !triedEndpoints.Contains(e.id)).ToList();
                var node = _loadBalancer.SelectEndpoint(candidates, strategy, currentTick);
                if (node == null) break;

                triedEndpoints.Add(node.id);

                if (node.providerType == ProviderType.LocalSubscriptionGateway)
                {
                    if (!node.IsLoopbackAddress)
                    {
                        node.RecordFailure(currentTick);
                        return Result<LlmResponse, RimMindError>.Err(
                            RimMindErrors.ClientPermanent("Security violation: Local subscription gateway must be on loopback (127.0.0.1)"));
                    }
                }

                var result = await TrySendToNodeAsync(envelope, node).ConfigureAwait(false);
                if (result.IsOk)
                {
                    return result;
                }

                lastErrorMessage = result.Error.Message;
                node.RecordFailure(currentTick);
            }

            return Result<LlmResponse, RimMindError>.Err(
                RimMindErrors.ClientTransient($"All candidate endpoints failed. Last error: {lastErrorMessage}"));
        }

        private async Task<Result<LlmResponse, RimMindError>> TrySendToNodeAsync(
            LlmRequestEnvelope envelope,
            ModelEndpointConfig node)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                string requestUrl;
                string payloadJson;
                using var request = new HttpRequestMessage(HttpMethod.Post, "");

                if (node.providerType == ProviderType.AnthropicClaude)
                {
                    requestUrl = node.endpoint.TrimEnd('/') + "/messages";
                    payloadJson = AnthropicProtocolAdapter.BuildRequestJson(envelope, node);

                    request.RequestUri = new Uri(requestUrl);
                    request.Headers.TryAddWithoutValidation("x-api-key", node.apiKey);
                    request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                }
                else // OpenAICompatible or LocalSubscriptionGateway
                {
                    requestUrl = node.endpoint.TrimEnd('/') + "/chat/completions";
                    payloadJson = OpenAIProtocolAdapter.BuildRequestJson(envelope, node);

                    request.RequestUri = new Uri(requestUrl);
                    if (!string.IsNullOrEmpty(node.apiKey))
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + node.apiKey);
                    }
                }

                using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
                request.Content = content;

                using var response = await HttpClient.SendAsync(request, envelope.Ct).ConfigureAwait(false);
                string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                stopwatch.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    return Result<LlmResponse, RimMindError>.Err(
                        RimMindErrors.ClientPermanent($"Node '{node.name}' HTTP {(int)response.StatusCode}: {responseBody}"));
                }

                Result<LlmResponse, RimMindError> parseResult;
                if (node.providerType == ProviderType.AnthropicClaude)
                {
                    parseResult = AnthropicProtocolAdapter.ParseResponse(responseBody);
                }
                else
                {
                    parseResult = OpenAIProtocolAdapter.ParseResponse(responseBody);
                }

                if (parseResult.IsOk)
                {
                    node.RecordSuccess((int)stopwatch.ElapsedMilliseconds);
                    var val = parseResult.Value;
                    string reqId = string.IsNullOrEmpty(val.RequestId) ? envelope.RequestId : val.RequestId;
                    val = val.With(requestId: reqId, state: AIRequestState.Completed);
                    parseResult = Result<LlmResponse, RimMindError>.Ok(val);
                }

                return parseResult;
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                return Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.ClientTransient("Request was canceled."));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.ClientTransient($"Failed to reach node '{node.name}': {ex.Message}"));
            }
        }

        public Task<Result<LlmResponse, RimMindError>> SendStreamAsync(
            LlmRequestEnvelope envelope,
            Action<LlmChunk> onChunk,
            CancellationToken ct = default)
        {
            return SendAsync(envelope);
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
