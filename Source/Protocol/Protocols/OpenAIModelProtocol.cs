using System;
using System.Net.Http;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    public class OpenAIModelProtocol : IModelProtocol
    {
        public static readonly OpenAIModelProtocol Instance = new OpenAIModelProtocol();

        public virtual string ProtocolId => ModelProtocolRegistry.OpenAIProtocolId;
        public virtual ProviderType LegacyProviderType => ProviderType.OpenAICompatible;
        public virtual string DisplayLabelKey => "RimMind.ModelService.Provider.OpenAICompatible";
        public virtual string FallbackLabel => "OpenAI Compatible";
        public virtual string DefaultEndpointUrl => "https://api.openai.com/v1";
        public virtual string DefaultModel => "gpt-4o-mini";
        public virtual ProtocolCapabilities Capabilities => ProtocolCapabilities.Default;

        public virtual Uri ResolveCompletionUri(ModelEndpointConfig endpoint)
        {
            string baseUrl = !string.IsNullOrWhiteSpace(endpoint?.endpoint)
                ? endpoint!.endpoint.Trim().TrimEnd('/')
                : DefaultEndpointUrl.TrimEnd('/');
            return new Uri(baseUrl + "/chat/completions");
        }

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
                request.RequestUri = ResolveCompletionUri(endpoint);
            }

            if (!string.IsNullOrEmpty(endpoint.apiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + endpoint.apiKey);
            }
        }

        public virtual HttpRequestMessage BuildHealthProbeRequest(ModelEndpointConfig endpoint, string resolvedUrl)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, resolvedUrl);
            ApplyHeaders(request, endpoint, "health-probe");
            return request;
        }
    }
}
