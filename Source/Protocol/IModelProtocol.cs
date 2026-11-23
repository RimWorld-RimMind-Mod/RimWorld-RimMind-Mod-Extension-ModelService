using System;
using System.Collections.Generic;
using System.Net.Http;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    public sealed class ProtocolEnvBinding
    {
        public ProtocolEnvBinding(
            string[] apiKeyVars,
            string[]? endpointVars = null,
            string[]? modelVars = null)
        {
            ApiKeyVars = apiKeyVars ?? Array.Empty<string>();
            EndpointVars = endpointVars ?? Array.Empty<string>();
            ModelVars = modelVars ?? Array.Empty<string>();
        }

        public string[] ApiKeyVars { get; }
        public string[] EndpointVars { get; }
        public string[] ModelVars { get; }
    }

    public sealed class ProtocolPresetDescriptor
    {
        public ProtocolPresetDescriptor(
            string buttonLabelKey,
            string buttonLabelFallback,
            string tooltipKey,
            string tooltipFallback,
            float buttonWidth,
            Func<int, ModelEndpointConfig> createEndpoint)
        {
            ButtonLabelKey = buttonLabelKey ?? string.Empty;
            ButtonLabelFallback = buttonLabelFallback ?? string.Empty;
            TooltipKey = tooltipKey ?? string.Empty;
            TooltipFallback = tooltipFallback ?? string.Empty;
            ButtonWidth = buttonWidth;
            CreateEndpoint = createEndpoint ?? throw new ArgumentNullException(nameof(createEndpoint));
        }

        public string ButtonLabelKey { get; }
        public string ButtonLabelFallback { get; }
        public string TooltipKey { get; }
        public string TooltipFallback { get; }
        public float ButtonWidth { get; }
        public Func<int, ModelEndpointConfig> CreateEndpoint { get; }
    }

    public sealed class ProtocolCapabilities
    {
        public ProtocolCapabilities(
            bool supportsNativeThinking = false,
            bool supportsSafetyCheck = false,
            bool supportsJsonSchema = true,
            bool requiresReasoningContentInToolHistory = false,
            bool requiresLoopback = false,
            string? statusBadgeLabelKey = null,
            string? statusBadgeFallback = null,
            string? statusBadgeTooltipKey = null,
            string? statusBadgeTooltipFallback = null)
        {
            SupportsNativeThinking = supportsNativeThinking;
            SupportsSafetyCheck = supportsSafetyCheck;
            SupportsJsonSchema = supportsJsonSchema;
            RequiresReasoningContentInToolHistory = requiresReasoningContentInToolHistory;
            RequiresLoopback = requiresLoopback;
            StatusBadgeLabelKey = statusBadgeLabelKey;
            StatusBadgeFallback = statusBadgeFallback;
            StatusBadgeTooltipKey = statusBadgeTooltipKey;
            StatusBadgeTooltipFallback = statusBadgeTooltipFallback;
        }

        public bool SupportsNativeThinking { get; }
        public bool SupportsSafetyCheck { get; }
        public bool SupportsJsonSchema { get; }
        public bool RequiresReasoningContentInToolHistory { get; }
        public bool RequiresLoopback { get; }
        public string? StatusBadgeLabelKey { get; }
        public string? StatusBadgeFallback { get; }
        public string? StatusBadgeTooltipKey { get; }
        public string? StatusBadgeTooltipFallback { get; }

        public bool HasStatusBadge =>
            !string.IsNullOrEmpty(StatusBadgeLabelKey) || !string.IsNullOrEmpty(StatusBadgeFallback);

        public static ProtocolCapabilities Default { get; } = new ProtocolCapabilities();
    }

    /// <summary>
    /// Unified pluggable contract for ModelService wire protocols.
    /// Encapsulates URI resolution, request serialization, response parsing, HTTP error classification,
    /// header decoration, health probing, optional preset metadata, and optional environment-overlay bindings.
    /// </summary>
    public interface IModelProtocol
    {
        string ProtocolId { get; }
        ProviderType LegacyProviderType { get; }
        IReadOnlyList<string> LegacyAliases { get; }
        string DisplayLabelKey { get; }
        string FallbackLabel { get; }
        string DefaultEndpointUrl { get; }
        string DefaultModel { get; }
        ProtocolCapabilities Capabilities { get; }
        ProtocolPresetDescriptor? PresetDescriptor { get; }
        ProtocolEnvBinding? EnvOverrideBinding { get; }

        Uri ResolveCompletionUri(ModelEndpointConfig endpoint);
        Uri ResolveHealthProbeUri(ModelEndpointConfig endpoint);
        Result<string, RimMindError> BuildRequestJson(LlmRequestEnvelope envelope, ModelEndpointConfig endpoint, string modelOverride);
        Result<LlmResponse, RimMindError> ParseResponse(string responseBody, LlmRequestEnvelope? envelope = null);
        RimMindError ClassifyHttpError(int statusCode, string sanitizedBody, ModelEndpointConfig endpoint);
        void ApplyHeaders(HttpRequestMessage request, ModelEndpointConfig endpoint, string? requestId);
        HttpRequestMessage BuildHealthProbeRequest(ModelEndpointConfig endpoint);
    }
}
