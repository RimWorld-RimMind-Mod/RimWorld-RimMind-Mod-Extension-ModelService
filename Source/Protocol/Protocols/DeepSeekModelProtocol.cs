using System;
using System.Net.Http;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Models;
using CoreHttpErrorClassifier = RimMind.Application.Features.Llm.HttpErrorClassifier;

namespace RimMind.ModelService.Protocol
{
    public sealed class DeepSeekModelProtocol : IContextualModelProtocol
    {
        public static readonly DeepSeekModelProtocol Instance = new DeepSeekModelProtocol();

        private static readonly ProtocolCapabilities ProtocolCaps = new ProtocolCapabilities(
            supportsNativeThinking: true,
            supportsSafetyCheck: true,
            supportsJsonSchema: false,
            requiresReasoningContentInToolHistory: true,
            requiresLoopback: false);

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

        public Uri ResolveCompletionUri(ModelEndpointConfig endpoint)
        {
            string baseUrl = ResolveBaseUrl(endpoint?.endpoint, DefaultEndpointUrl);
            return new Uri(baseUrl + "/chat/completions");
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
            CoreHttpErrorClassifier.Classify(statusCode, sanitizedBody, endpoint?.name ?? FallbackLabel);

        public void ApplyHeaders(HttpRequestMessage request, ModelEndpointConfig endpoint, string? requestId)
        {
            if (request == null || endpoint == null) return;

            if (request.Method == HttpMethod.Post && !string.IsNullOrWhiteSpace(endpoint.endpoint))
            {
                request.RequestUri = ResolveCompletionUri(endpoint);
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
