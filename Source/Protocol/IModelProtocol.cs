using System;
using System.Net.Http;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    public interface IModelProtocol
    {
        string ProtocolId { get; }
        ProviderType LegacyProviderType { get; }
        string DisplayLabelKey { get; }
        string FallbackLabel { get; }
        string DefaultEndpointUrl { get; }
        string DefaultModel { get; }
        ProtocolCapabilities Capabilities { get; }
        Uri ResolveCompletionUri(ModelEndpointConfig endpoint);
        Result<string, RimMindError> BuildRequestJson(LlmRequestEnvelope envelope, string modelOverride);
        Result<LlmResponse, RimMindError> ParseResponse(string responseBody, LlmRequestEnvelope? envelope = null);
        void ApplyHeaders(HttpRequestMessage request, ModelEndpointConfig endpoint, string? requestId);
        HttpRequestMessage BuildHealthProbeRequest(ModelEndpointConfig endpoint, string resolvedUrl);
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

    public interface IContextualModelProtocol : IModelProtocol
    {
        Result<string, RimMindError> BuildRequestJson(LlmRequestEnvelope envelope, ModelEndpointConfig endpoint, string modelOverride);
        RimMindError ClassifyHttpError(int statusCode, string sanitizedBody, ModelEndpointConfig endpoint);
    }
}
