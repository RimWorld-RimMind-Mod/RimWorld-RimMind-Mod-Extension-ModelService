using System;
using System.Collections.Generic;
using System.Net.Http;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Models;
using CoreHttpErrorClassifier = RimMind.Application.Features.Llm.HttpErrorClassifier;

namespace RimMind.ModelService.Protocol
{
    public class OpenAIModelProtocol : IModelProtocol
    {
        public static readonly OpenAIModelProtocol Instance = new OpenAIModelProtocol();

        private static readonly string[] DefaultAliases = { nameof(ProviderType.OpenAICompatible) };

        public virtual string ProtocolId => ModelProtocolRegistry.OpenAIProtocolId;
        public virtual ProviderType LegacyProviderType => ProviderType.OpenAICompatible;
        public virtual IReadOnlyList<string> LegacyAliases =>
            GetType() == typeof(OpenAIModelProtocol) ? DefaultAliases : Array.Empty<string>();
        public virtual string DisplayLabelKey => "RimMind.ModelService.Provider.OpenAICompatible";
        public virtual string FallbackLabel => "OpenAI Compatible";
        public virtual string DefaultEndpointUrl => "https://api.openai.com/v1";
        public virtual string DefaultModel => "gpt-4o-mini";
        public virtual ProtocolCapabilities Capabilities => ProtocolCapabilities.Default;
        public virtual ProtocolPresetDescriptor? PresetDescriptor => null;
        public virtual ProtocolEnvBinding? EnvOverrideBinding => null;

        internal static string NormalizeBaseUrl(string? rawEndpoint, string fallback)
        {
            string url = !string.IsNullOrWhiteSpace(rawEndpoint)
                ? rawEndpoint!.Trim().TrimEnd('/')
                : fallback.TrimEnd('/');
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring(0, url.Length - "/chat/completions".Length).TrimEnd('/');
            }
            return url;
        }

        public virtual Uri ResolveCompletionUri(ModelEndpointConfig endpoint)
        {
            string baseUrl = NormalizeBaseUrl(endpoint?.endpoint, DefaultEndpointUrl);
            return new Uri(baseUrl + "/chat/completions");
        }

        public virtual Uri ResolveHealthProbeUri(ModelEndpointConfig endpoint)
        {
            string baseUrl = NormalizeBaseUrl(endpoint?.endpoint, DefaultEndpointUrl);
            return new Uri(baseUrl + "/models");
        }

        public virtual Result<string, RimMindError> BuildRequestJson(
            LlmRequestEnvelope envelope,
            ModelEndpointConfig endpoint,
            string modelOverride)
        {
            if (envelope == null)
                return Result<string, RimMindError>.Err(RimMindErrors.ClientPermanent("Request envelope is null."));

            string effectiveModel = !string.IsNullOrEmpty(modelOverride)
                ? modelOverride
                : (!string.IsNullOrEmpty(endpoint?.modelName) ? endpoint!.modelName : DefaultModel);
            return Result<string, RimMindError>.Ok(OpenAIProtocolAdapter.BuildRequestJson(envelope, effectiveModel));
        }

        public virtual Result<LlmResponse, RimMindError> ParseResponse(string responseBody, LlmRequestEnvelope? envelope = null)
        {
            return OpenAIProtocolAdapter.ParseResponse(responseBody, envelope);
        }

        public virtual RimMindError ClassifyHttpError(int statusCode, string sanitizedBody, ModelEndpointConfig endpoint)
        {
            return CoreHttpErrorClassifier.Classify(statusCode, sanitizedBody, endpoint?.name ?? FallbackLabel);
        }

        public virtual void ApplyHeaders(HttpRequestMessage request, ModelEndpointConfig endpoint, string? requestId)
        {
            if (request == null || endpoint == null) return;

            if (!string.IsNullOrEmpty(endpoint.apiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + endpoint.apiKey);
            }
        }

        public virtual HttpRequestMessage BuildHealthProbeRequest(ModelEndpointConfig endpoint)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, ResolveHealthProbeUri(endpoint));
            ApplyHeaders(request, endpoint, "health-probe");
            return request;
        }
    }
}
