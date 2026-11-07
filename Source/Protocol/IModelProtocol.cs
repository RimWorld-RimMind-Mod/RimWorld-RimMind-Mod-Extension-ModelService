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
}
