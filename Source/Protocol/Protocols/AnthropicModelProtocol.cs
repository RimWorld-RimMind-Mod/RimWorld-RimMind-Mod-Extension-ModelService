using System;
using System.Net.Http;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    public sealed class AnthropicModelProtocol : IModelProtocol
    {
        public static readonly AnthropicModelProtocol Instance = new AnthropicModelProtocol();

        private static readonly ProtocolCapabilities ProtocolCaps = new ProtocolCapabilities(
            supportsNativeThinking: false,
            supportsSafetyCheck: false,
            supportsJsonSchema: false,
            requiresReasoningContentInToolHistory: false,
            requiresLoopback: false);

        public string ProtocolId => ModelProtocolRegistry.AnthropicProtocolId;
        public ProviderType LegacyProviderType => ProviderType.AnthropicClaude;
        public string DisplayLabelKey => "RimMind.ModelService.Provider.AnthropicClaude";
        public string FallbackLabel => "Anthropic Claude Native";
        public string DefaultEndpointUrl => "https://api.anthropic.com/v1";
        public string DefaultModel => "claude-3-5-haiku-20241022";
        public ProtocolCapabilities Capabilities => ProtocolCaps;

        public Uri ResolveCompletionUri(ModelEndpointConfig endpoint)
        {
            string baseUrl = !string.IsNullOrWhiteSpace(endpoint?.endpoint)
                ? endpoint!.endpoint.Trim().TrimEnd('/')
                : DefaultEndpointUrl.TrimEnd('/');
            return new Uri(baseUrl + "/messages");
        }

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
                request.RequestUri = ResolveCompletionUri(endpoint);
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
}
