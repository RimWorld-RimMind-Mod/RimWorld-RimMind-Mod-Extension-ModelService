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
            bool requiresReasoningContentInToolHistory = false)
        {
            SupportsNativeThinking = supportsNativeThinking;
            SupportsSafetyCheck = supportsSafetyCheck;
            SupportsJsonSchema = supportsJsonSchema;
            RequiresReasoningContentInToolHistory = requiresReasoningContentInToolHistory;
        }

        public bool SupportsNativeThinking { get; }
        public bool SupportsSafetyCheck { get; }
        public bool SupportsJsonSchema { get; }
        public bool RequiresReasoningContentInToolHistory { get; }

        public static ProtocolCapabilities Default { get; } = new ProtocolCapabilities();
    }

    public interface IContextualModelProtocol : IModelProtocol
    {
        ProtocolCapabilities Capabilities { get; }
        Result<string, RimMindError> BuildRequestJson(LlmRequestEnvelope envelope, ModelEndpointConfig endpoint, string modelOverride);
        RimMindError ClassifyHttpError(int statusCode, string sanitizedBody, ModelEndpointConfig endpoint);
    }
}
