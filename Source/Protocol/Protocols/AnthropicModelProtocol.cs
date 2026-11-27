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
    public sealed class AnthropicModelProtocol : IModelProtocol
    {
        public static readonly AnthropicModelProtocol Instance = new AnthropicModelProtocol();

        private const string AnthropicApiVersion = "2023-06-01";
        private static readonly string[] Aliases = { nameof(ProviderType.AnthropicClaude) };

        private static readonly ProtocolCapabilities ProtocolCaps = new ProtocolCapabilities(
            supportsNativeThinking: false,
            supportsSafetyCheck: false,
            supportsJsonSchema: false,
            requiresReasoningContentInToolHistory: false,
            requiresLoopback: false,
            supportsVision: true);

        public string ProtocolId => ModelProtocolRegistry.AnthropicProtocolId;
        public ProviderType LegacyProviderType => ProviderType.AnthropicClaude;
        public IReadOnlyList<string> LegacyAliases => Aliases;
        public string DisplayLabelKey => "RimMind.ModelService.Provider.AnthropicClaude";
        public string FallbackLabel => "Anthropic Claude Native";
        public string DefaultEndpointUrl => "https://api.anthropic.com/v1";
        public string DefaultModel => "claude-3-5-haiku-20241022";
        public ProtocolCapabilities Capabilities => ProtocolCaps;
        public ProtocolPresetDescriptor? PresetDescriptor => null;
        public ProtocolEnvBinding? EnvOverrideBinding => null;

        internal static string NormalizeBaseUrl(string? rawEndpoint, string fallback)
        {
            string url = !string.IsNullOrWhiteSpace(rawEndpoint)
                ? rawEndpoint!.Trim().TrimEnd('/')
                : fallback.TrimEnd('/');
            if (url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring(0, url.Length - "/messages".Length).TrimEnd('/');
            }
            return url;
        }

        public Uri ResolveCompletionUri(ModelEndpointConfig endpoint)
        {
            string baseUrl = NormalizeBaseUrl(endpoint?.endpoint, DefaultEndpointUrl);
            return new Uri(baseUrl + "/messages");
        }

        public Uri ResolveHealthProbeUri(ModelEndpointConfig endpoint)
        {
            string baseUrl = NormalizeBaseUrl(endpoint?.endpoint, DefaultEndpointUrl);
            return new Uri(baseUrl + "/models");
        }

        public Result<string, RimMindError> BuildRequestJson(
            LlmRequestEnvelope envelope,
            ModelEndpointConfig endpoint,
            string modelOverride)
        {
            if (envelope == null)
                return Result<string, RimMindError>.Err(RimMindErrors.ClientPermanent("Request envelope is null."));

            string effectiveModel = !string.IsNullOrEmpty(modelOverride)
                ? modelOverride
                : (!string.IsNullOrEmpty(endpoint?.modelName) ? endpoint!.modelName : DefaultModel);
            return Result<string, RimMindError>.Ok(
                AnthropicProtocolAdapter.BuildRequestJson(
                    envelope,
                    effectiveModel,
                    allowVisionAttachments: Capabilities.SupportsVision));
        }

        public Result<LlmResponse, RimMindError> ParseResponse(string responseBody, LlmRequestEnvelope? envelope = null)
        {
            return AnthropicProtocolAdapter.ParseResponse(responseBody);
        }

        public RimMindError ClassifyHttpError(int statusCode, string sanitizedBody, ModelEndpointConfig endpoint)
        {
            return CoreHttpErrorClassifier.Classify(statusCode, sanitizedBody, endpoint?.name ?? FallbackLabel);
        }

        public void ApplyHeaders(HttpRequestMessage request, ModelEndpointConfig endpoint, string? requestId)
        {
            if (request == null || endpoint == null) return;

            request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicApiVersion);

            if (!string.IsNullOrEmpty(endpoint.apiKey))
            {
                request.Headers.TryAddWithoutValidation("x-api-key", endpoint.apiKey);
            }
            else if (request.Method == HttpMethod.Post)
            {
                request.Headers.TryAddWithoutValidation("x-api-key", string.Empty);
            }
        }

        public HttpRequestMessage BuildHealthProbeRequest(ModelEndpointConfig endpoint)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, ResolveHealthProbeUri(endpoint));
            ApplyHeaders(request, endpoint, "health-probe");
            return request;
        }
    }
}
