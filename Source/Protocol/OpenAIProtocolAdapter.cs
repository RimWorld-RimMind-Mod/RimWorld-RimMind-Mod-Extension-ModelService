using System;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    /// <summary>
    /// OpenAI chat-completions wire adapter for ModelService endpoints.
    /// Delegates directly to Core's public <see cref="OpenAIWireCodec"/> so request serialization
    /// and response parsing stay 100% shared with Core without touching Core Infrastructure.
    /// </summary>
    public static class OpenAIProtocolAdapter
    {
        private const string DefaultModelName = "gpt-4o-mini";

        public static string BuildRequestJson(
            LlmRequestEnvelope envelope,
            ModelEndpointConfig node,
            bool allowVisionAttachments = true)
        {
            return BuildRequestJson(envelope, node?.modelName, allowVisionAttachments);
        }

        public static string BuildRequestJson(
            LlmRequestEnvelope envelope,
            string? modelName,
            bool allowVisionAttachments = true)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            string effectiveModel = !string.IsNullOrEmpty(modelName)
                ? modelName!
                : DefaultModelName;

            return OpenAIWireCodec.BuildRequestJson(
                envelope,
                effectiveModel,
                defaultMaxTokens: 0,
                stream: false,
                forceJsonMode: false,
                omitZeroMaxTokens: true,
                allowVisionAttachments: allowVisionAttachments);
        }

        public static Result<LlmResponse, RimMindError> ParseResponse(string responseJson)
            => OpenAIWireCodec.ParseResponse(responseJson, null);

        public static Result<LlmResponse, RimMindError> ParseResponse(string responseJson, LlmRequestEnvelope? envelope)
            => OpenAIWireCodec.ParseResponse(responseJson, envelope);
    }
}
