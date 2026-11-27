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
    public sealed class DeepSeekModelProtocol : IModelProtocol
    {
        public static readonly DeepSeekModelProtocol Instance = new DeepSeekModelProtocol();

        private static readonly string[] Aliases = { nameof(ProviderType.DeepSeekOfficial), "deepseek_native" };

        private static readonly ProtocolCapabilities ProtocolCaps = new ProtocolCapabilities(
            supportsNativeThinking: true,
            supportsSafetyCheck: true,
            supportsJsonSchema: false,
            requiresReasoningContentInToolHistory: true,
            requiresLoopback: false,
            supportsVision: true);

        private static readonly ProtocolEnvBinding EnvBinding = new ProtocolEnvBinding(
            apiKeyVars: new[] { "RIMMIND_DEEPSEEK_API_KEY", "DEEPSEEK_API_KEY" },
            endpointVars: new[] { "RIMMIND_DEEPSEEK_ENDPOINT" },
            modelVars: new[] { "RIMMIND_DEEPSEEK_MODEL" });

        private static readonly ProtocolPresetDescriptor Preset = new ProtocolPresetDescriptor(
            buttonLabelKey: "RimMind.ModelService.Settings.AddDeepSeekPreset",
            buttonLabelFallback: "+ DeepSeek Preset",
            tooltipKey: "RimMind.ModelService.Settings.AddDeepSeekPreset.Desc",
            tooltipFallback: "Quickly add DeepSeek official API node with adaptive thinking & high safety mode.",
            buttonWidth: 145f,
            createEndpoint: priority => new ModelEndpointConfig
            {
                id = Guid.NewGuid().ToString("N"),
                name = "DeepSeek Official",
                endpoint = Instance.DefaultEndpointUrl,
                modelName = Instance.DefaultModel,
                protocolId = ModelProtocolRegistry.DeepSeekProtocolId,
                providerType = ProviderType.DeepSeekOfficial,
                timeoutSeconds = 60,
                isEnabled = false,
                thinkingPolicy = EndpointThinkingPolicy.FollowRequest,
                safetyLevel = EndpointSafetyLevel.High,
                priority = priority,
                weight = 1
            });

        public string ProtocolId => ModelProtocolRegistry.DeepSeekProtocolId;
        public ProviderType LegacyProviderType => ProviderType.DeepSeekOfficial;
        public IReadOnlyList<string> LegacyAliases => Aliases;
        public string DisplayLabelKey => "RimMind.ModelService.Provider.DeepSeekOfficial";
        public string FallbackLabel => "DeepSeek Official";
        public string DefaultEndpointUrl => "https://api.deepseek.com";
        public string DefaultModel => DeepSeekProtocolAdapter.DefaultModelName;
        public ProtocolCapabilities Capabilities => ProtocolCaps;
        public ProtocolPresetDescriptor? PresetDescriptor => Preset;
        public ProtocolEnvBinding? EnvOverrideBinding => EnvBinding;

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

        public Uri ResolveHealthProbeUri(ModelEndpointConfig endpoint)
        {
            string baseUrl = ResolveBaseUrl(endpoint?.endpoint, DefaultEndpointUrl);
            return new Uri(baseUrl + "/models");
        }

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

            if (!string.IsNullOrEmpty(endpoint.apiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + endpoint.apiKey);
            }
        }

        public HttpRequestMessage BuildHealthProbeRequest(ModelEndpointConfig endpoint)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, ResolveHealthProbeUri(endpoint));
            if (endpoint != null)
            {
                ApplyHeaders(request, endpoint, "health-probe");
            }
            return request;
        }
    }
}
