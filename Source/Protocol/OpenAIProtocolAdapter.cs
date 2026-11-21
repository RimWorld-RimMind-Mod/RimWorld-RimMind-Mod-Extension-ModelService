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

        public static string BuildRequestJson(LlmRequestEnvelope envelope, ModelEndpointConfig node)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            string modelName = node != null && !string.IsNullOrEmpty(node.modelName)
                ? node.modelName
                : DefaultModelName;

            return OpenAIWireCodec.BuildRequestJson(
                envelope,
                modelName,
                defaultMaxTokens: 0,
                stream: false,
                forceJsonMode: false,
                omitZeroMaxTokens: true);
        }

        public static Result<LlmResponse, RimMindError> ParseResponse(string responseJson)
            => OpenAIWireCodec.ParseResponse(responseJson, null);

        public static Result<LlmResponse, RimMindError> ParseResponse(string responseJson, LlmRequestEnvelope? envelope)
            => OpenAIWireCodec.ParseResponse(responseJson, envelope);
    }
}
