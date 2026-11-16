using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    public static class ModelProtocolRegistry
    {
        public const string OpenAIProtocolId = "openai";
        public const string AnthropicProtocolId = "anthropic";
        public const string LocalGatewayProtocolId = "local_gateway";
        public const string OpenCodeGoProtocolId = "opencode_go";
        public const string DeepSeekProtocolId = "deepseek";

        private static readonly object SyncRoot = new object();
        private static readonly List<IModelProtocol> OrderedProtocols = new List<IModelProtocol>();
        private static readonly Dictionary<string, IModelProtocol> ProtocolsById =
            new Dictionary<string, IModelProtocol>(StringComparer.OrdinalIgnoreCase);

        static ModelProtocolRegistry()
        {
            ResetToDefaults();
        }

        public static IReadOnlyList<IModelProtocol> All
        {
            get
            {
                lock (SyncRoot)
                {
                    return OrderedProtocols.ToArray();
                }
            }
        }

        public static void ResetToDefaults()
        {
            lock (SyncRoot)
            {
                OrderedProtocols.Clear();
                ProtocolsById.Clear();

                RegisterCore(OpenAIModelProtocol.Instance);
                RegisterCore(AnthropicModelProtocol.Instance);
                RegisterCore(LocalGatewayModelProtocol.Instance);
                RegisterCore(OpenCodeGoModelProtocol.Instance);
                RegisterCore(DeepSeekModelProtocol.Instance);
            }
        }

        public static void Register(IModelProtocol protocol)
        {
            if (protocol == null) throw new ArgumentNullException(nameof(protocol));
            if (string.IsNullOrWhiteSpace(protocol.ProtocolId))
                throw new ArgumentException("ProtocolId cannot be null or whitespace.", nameof(protocol));

            lock (SyncRoot)
            {
                RegisterCore(protocol);
            }
        }

        public static bool Unregister(string protocolId)
        {
            if (string.IsNullOrWhiteSpace(protocolId)) return false;

            string key = protocolId.Trim();
            lock (SyncRoot)
            {
                if (!ProtocolsById.TryGetValue(key, out var existing))
                    return false;

                ProtocolsById.Remove(key);
                OrderedProtocols.RemoveAll(p => string.Equals(p.ProtocolId, existing.ProtocolId, StringComparison.OrdinalIgnoreCase));
                return true;
            }
        }

        public static bool TryGet(string? protocolId, out IModelProtocol protocol)
        {
            protocol = null!;
            if (string.IsNullOrWhiteSpace(protocolId)) return false;

            string key = NormalizeAlias(protocolId!.Trim());
            lock (SyncRoot)
            {
                return ProtocolsById.TryGetValue(key, out protocol!);
            }
        }

        public static IModelProtocol? FindById(string? protocolId) =>
            TryGet(protocolId, out var protocol) ? protocol : null;

        public static IModelProtocol Resolve(ModelEndpointConfig? endpoint)
        {
            if (endpoint == null)
                return OpenAIModelProtocol.Instance;

            return Resolve(endpoint.EffectiveProtocolId, endpoint.providerType);
        }

        public static IModelProtocol Resolve(string? protocolId, ProviderType fallbackProviderType = ProviderType.OpenAICompatible)
        {
            if (TryGet(protocolId, out var protocol))
                return protocol;

            string fallbackId = ToProtocolId(fallbackProviderType);
            if (TryGet(fallbackId, out var fallbackProtocol))
                return fallbackProtocol;

            return OpenAIModelProtocol.Instance;
        }

        public static string ToProtocolId(ProviderType providerType) => providerType switch
        {
            ProviderType.OpenAICompatible => OpenAIProtocolId,
            ProviderType.AnthropicClaude => AnthropicProtocolId,
            ProviderType.LocalSubscriptionGateway => LocalGatewayProtocolId,
            ProviderType.OpenCodeGo => OpenCodeGoProtocolId,
            ProviderType.DeepSeekOfficial => DeepSeekProtocolId,
            _ => OpenAIProtocolId
        };

        public static bool TryGetBuiltInProviderType(string? protocolId, out ProviderType providerType)
        {
            providerType = ProviderType.OpenAICompatible;
            if (string.IsNullOrWhiteSpace(protocolId))
                return false;

            string normalized = NormalizeAlias(protocolId!.Trim());
            if (string.Equals(normalized, OpenAIProtocolId, StringComparison.OrdinalIgnoreCase))
            {
                providerType = ProviderType.OpenAICompatible;
                return true;
            }
            if (string.Equals(normalized, AnthropicProtocolId, StringComparison.OrdinalIgnoreCase))
            {
                providerType = ProviderType.AnthropicClaude;
                return true;
            }
            if (string.Equals(normalized, LocalGatewayProtocolId, StringComparison.OrdinalIgnoreCase))
            {
                providerType = ProviderType.LocalSubscriptionGateway;
                return true;
            }
            if (string.Equals(normalized, OpenCodeGoProtocolId, StringComparison.OrdinalIgnoreCase))
            {
                providerType = ProviderType.OpenCodeGo;
                return true;
            }
            if (string.Equals(normalized, DeepSeekProtocolId, StringComparison.OrdinalIgnoreCase))
            {
                providerType = ProviderType.DeepSeekOfficial;
                return true;
            }
            return false;
        }

        private static string NormalizeAlias(string id)
        {
            if (string.Equals(id, nameof(ProviderType.OpenAICompatible), StringComparison.OrdinalIgnoreCase))
                return OpenAIProtocolId;
            if (string.Equals(id, nameof(ProviderType.AnthropicClaude), StringComparison.OrdinalIgnoreCase))
                return AnthropicProtocolId;
            if (string.Equals(id, nameof(ProviderType.LocalSubscriptionGateway), StringComparison.OrdinalIgnoreCase))
                return LocalGatewayProtocolId;
            if (string.Equals(id, nameof(ProviderType.OpenCodeGo), StringComparison.OrdinalIgnoreCase))
                return OpenCodeGoProtocolId;
            if (string.Equals(id, nameof(ProviderType.DeepSeekOfficial), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "deepseek_native", StringComparison.OrdinalIgnoreCase))
                return DeepSeekProtocolId;
            return id;
        }

        private static void RegisterCore(IModelProtocol protocol)
        {
            string key = protocol.ProtocolId.Trim();
            int existingIndex = OrderedProtocols.FindIndex(p =>
                string.Equals(p.ProtocolId, key, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                OrderedProtocols[existingIndex] = protocol;
            }
            else
            {
                OrderedProtocols.Add(protocol);
            }
            ProtocolsById[key] = protocol;
        }

        internal static void ApplyOpenCodeSessionHeader(HttpRequestMessage request, string? requestId, int fallbackGuidLength)
        {
            string sessionId = !string.IsNullOrEmpty(requestId)
                ? requestId!
                : Guid.NewGuid().ToString("N").Substring(0, fallbackGuidLength);
            string headerValue = sessionId.StartsWith("rimmind-", StringComparison.OrdinalIgnoreCase)
                ? sessionId
                : "rimmind-" + sessionId;
            request.Headers.TryAddWithoutValidation("x-opencode-session", headerValue);
        }
    }

    public class OpenAIModelProtocol : IModelProtocol
    {
        public static readonly OpenAIModelProtocol Instance = new OpenAIModelProtocol();

        public virtual string ProtocolId => ModelProtocolRegistry.OpenAIProtocolId;
        public virtual ProviderType LegacyProviderType => ProviderType.OpenAICompatible;
        public virtual string DisplayLabelKey => "RimMind.ModelService.Provider.OpenAICompatible";
        public virtual string FallbackLabel => "OpenAI Compatible";
        public virtual string DefaultEndpointUrl => "https://api.openai.com/v1";
        public virtual string DefaultModel => "gpt-4o-mini";

        public virtual Result<string, RimMindError> BuildRequestJson(LlmRequestEnvelope envelope, string modelOverride)
        {
            if (envelope == null)
                return Result<string, RimMindError>.Err(RimMindErrors.ClientPermanent("Request envelope is null."));

            string effectiveModel = !string.IsNullOrEmpty(modelOverride) ? modelOverride : DefaultModel;
            var node = new ModelEndpointConfig { modelName = effectiveModel, protocolId = ProtocolId, providerType = LegacyProviderType };
            return Result<string, RimMindError>.Ok(OpenAIProtocolAdapter.BuildRequestJson(envelope, node));
        }

        public virtual Result<LlmResponse, RimMindError> ParseResponse(string responseBody, LlmRequestEnvelope? envelope = null)
        {
            return OpenAIProtocolAdapter.ParseResponse(responseBody, envelope);
        }

        public virtual void ApplyHeaders(HttpRequestMessage request, ModelEndpointConfig endpoint, string? requestId)
        {
            if (request == null || endpoint == null) return;

            if (request.RequestUri == null && !string.IsNullOrWhiteSpace(endpoint.endpoint))
            {
                request.RequestUri = new Uri(endpoint.endpoint.TrimEnd('/') + "/chat/completions");
            }

            if (!string.IsNullOrEmpty(endpoint.apiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + endpoint.apiKey);
            }

            string urlStr = request.RequestUri?.OriginalString ?? endpoint.endpoint ?? string.Empty;
            if ((!string.IsNullOrEmpty(urlStr) && urlStr.IndexOf("opencode", StringComparison.OrdinalIgnoreCase) >= 0)
                || (!string.IsNullOrEmpty(endpoint.apiKey) && endpoint.apiKey.IndexOf("oc_sk_", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                ModelProtocolRegistry.ApplyOpenCodeSessionHeader(request, requestId, fallbackGuidLength: 12);
            }
        }

        public virtual HttpRequestMessage BuildHealthProbeRequest(ModelEndpointConfig endpoint, string resolvedUrl)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, resolvedUrl);
            ApplyHeaders(request, endpoint, "health-probe");
            return request;
        }
    }

    public sealed class LocalGatewayModelProtocol : OpenAIModelProtocol
    {
        public static new readonly LocalGatewayModelProtocol Instance = new LocalGatewayModelProtocol();

        public override string ProtocolId => ModelProtocolRegistry.LocalGatewayProtocolId;
        public override ProviderType LegacyProviderType => ProviderType.LocalSubscriptionGateway;
        public override string DisplayLabelKey => "RimMind.ModelService.Provider.LocalSubscriptionGateway";
        public override string FallbackLabel => "Local Gateway (sub2api)";
        public override string DefaultEndpointUrl => "http://127.0.0.1:8000/v1";
        public override string DefaultModel => "gpt-4o";
    }

    public sealed class OpenCodeGoModelProtocol : OpenAIModelProtocol
    {
        public static new readonly OpenCodeGoModelProtocol Instance = new OpenCodeGoModelProtocol();

        public override string ProtocolId => ModelProtocolRegistry.OpenCodeGoProtocolId;
        public override ProviderType LegacyProviderType => ProviderType.OpenCodeGo;
        public override string DisplayLabelKey => "RimMind.ModelService.Provider.OpenCodeGo";
        public override string FallbackLabel => "OpenCode Go (Subscription)";
        public override string DefaultEndpointUrl => "https://opencode.ai/zen/go/v1";
        public override string DefaultModel => "deepseek-v4.1-flash";

        public override void ApplyHeaders(HttpRequestMessage request, ModelEndpointConfig endpoint, string? requestId)
        {
            if (request == null || endpoint == null) return;

            if (request.RequestUri == null && !string.IsNullOrWhiteSpace(endpoint.endpoint))
            {
                request.RequestUri = new Uri(endpoint.endpoint.TrimEnd('/') + "/chat/completions");
            }

            if (!string.IsNullOrEmpty(endpoint.apiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + endpoint.apiKey);
            }

            ModelProtocolRegistry.ApplyOpenCodeSessionHeader(request, requestId, fallbackGuidLength: 16);
        }
    }

    public sealed class AnthropicModelProtocol : IModelProtocol
    {
        public static readonly AnthropicModelProtocol Instance = new AnthropicModelProtocol();

        public string ProtocolId => ModelProtocolRegistry.AnthropicProtocolId;
        public ProviderType LegacyProviderType => ProviderType.AnthropicClaude;
        public string DisplayLabelKey => "RimMind.ModelService.Provider.AnthropicClaude";
        public string FallbackLabel => "Anthropic Claude Native";
        public string DefaultEndpointUrl => "https://api.anthropic.com/v1";
        public string DefaultModel => "claude-3-5-haiku-20241022";

        public Result<string, RimMindError> BuildRequestJson(LlmRequestEnvelope envelope, string modelOverride)
        {
            if (envelope == null)
                return Result<string, RimMindError>.Err(RimMindErrors.ClientPermanent("Request envelope is null."));

            string effectiveModel = !string.IsNullOrEmpty(modelOverride) ? modelOverride : DefaultModel;
            var node = new ModelEndpointConfig { modelName = effectiveModel, protocolId = ProtocolId, providerType = LegacyProviderType };
            return Result<string, RimMindError>.Ok(AnthropicProtocolAdapter.BuildRequestJson(envelope, node));
        }

        public Result<LlmResponse, RimMindError> ParseResponse(string responseBody, LlmRequestEnvelope? envelope = null)
        {
            return AnthropicProtocolAdapter.ParseResponse(responseBody);
        }

        public void ApplyHeaders(HttpRequestMessage request, ModelEndpointConfig endpoint, string? requestId)
        {
            if (request == null || endpoint == null) return;

            if (request.Method == HttpMethod.Post && !string.IsNullOrWhiteSpace(endpoint.endpoint))
            {
                request.RequestUri = new Uri(endpoint.endpoint.TrimEnd('/') + "/messages");
            }

            if (!string.IsNullOrEmpty(endpoint.apiKey))
            {
                request.Headers.TryAddWithoutValidation("x-api-key", endpoint.apiKey);
                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            }
            else if (request.Method == HttpMethod.Post)
            {
                request.Headers.TryAddWithoutValidation("x-api-key", string.Empty);
                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            }
        }

        public HttpRequestMessage BuildHealthProbeRequest(ModelEndpointConfig endpoint, string resolvedUrl)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, resolvedUrl);
            ApplyHeaders(request, endpoint, "health-probe");
            return request;
        }
    }

    public sealed class DeepSeekModelProtocol : IContextualModelProtocol
    {
        public static readonly DeepSeekModelProtocol Instance = new DeepSeekModelProtocol();

        private static readonly ProtocolCapabilities ProtocolCaps = new ProtocolCapabilities(
            supportsNativeThinking: true,
            supportsSafetyCheck: true,
            supportsJsonSchema: false,
            requiresReasoningContentInToolHistory: true);

        public string ProtocolId => ModelProtocolRegistry.DeepSeekProtocolId;
        public ProviderType LegacyProviderType => ProviderType.DeepSeekOfficial;
        public string DisplayLabelKey => "RimMind.ModelService.Provider.DeepSeekOfficial";
        public string FallbackLabel => "DeepSeek Official";
        public string DefaultEndpointUrl => "https://api.deepseek.com";
        public string DefaultModel => DeepSeekProtocolAdapter.DefaultModelName;
        public ProtocolCapabilities Capabilities => ProtocolCaps;

        internal static string ResolveBaseUrl(string? rawEndpoint, string fallback)
        {
            string url = string.IsNullOrWhiteSpace(rawEndpoint)
                ? fallback
                : rawEndpoint!.Trim().TrimEnd('/');
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring(0, url.Length - "/chat/completions".Length).TrimEnd('/');
            }
            return url;
        }

        public Result<string, RimMindError> BuildRequestJson(LlmRequestEnvelope envelope, string modelOverride) =>
            BuildRequestJson(envelope, new ModelEndpointConfig { modelName = modelOverride }, modelOverride);

        public Result<string, RimMindError> BuildRequestJson(
            LlmRequestEnvelope envelope,
            ModelEndpointConfig endpoint,
            string modelOverride)
        {
            if (envelope == null)
                return Result<string, RimMindError>.Err(RimMindErrors.ClientPermanent("Request envelope is null."));

            return Result<string, RimMindError>.Ok(
                DeepSeekProtocolAdapter.BuildRequestJson(envelope, endpoint, modelOverride));
        }

        public Result<LlmResponse, RimMindError> ParseResponse(string responseBody, LlmRequestEnvelope? envelope = null) =>
            DeepSeekProtocolAdapter.ParseResponse(responseBody, envelope);

        public RimMindError ClassifyHttpError(int statusCode, string sanitizedBody, ModelEndpointConfig endpoint) =>
            HttpErrorClassifier.ClassifyDefault(statusCode, sanitizedBody, endpoint?.name ?? FallbackLabel);

        public void ApplyHeaders(HttpRequestMessage request, ModelEndpointConfig endpoint, string? requestId)
        {
            if (request == null || endpoint == null) return;

            if (request.Method == HttpMethod.Post && !string.IsNullOrWhiteSpace(endpoint.endpoint))
            {
                string baseUrl = ResolveBaseUrl(endpoint.endpoint, DefaultEndpointUrl);
                request.RequestUri = new Uri(baseUrl + "/chat/completions");
            }
            else if (request.RequestUri == null && !string.IsNullOrWhiteSpace(endpoint.endpoint))
            {
                string baseUrl = ResolveBaseUrl(endpoint.endpoint, DefaultEndpointUrl);
                request.RequestUri = new Uri(baseUrl + "/models");
            }

            if (!string.IsNullOrEmpty(endpoint.apiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + endpoint.apiKey);
            }
        }

        public HttpRequestMessage BuildHealthProbeRequest(ModelEndpointConfig endpoint, string resolvedUrl)
        {
            string baseUrl = endpoint != null && !string.IsNullOrWhiteSpace(endpoint.endpoint)
                ? ResolveBaseUrl(endpoint.endpoint, DefaultEndpointUrl)
                : ResolveBaseUrl(resolvedUrl, DefaultEndpointUrl);
            var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/models");
            if (endpoint != null)
            {
                ApplyHeaders(request, endpoint, "health-probe");
            }
            return request;
        }
    }
}
