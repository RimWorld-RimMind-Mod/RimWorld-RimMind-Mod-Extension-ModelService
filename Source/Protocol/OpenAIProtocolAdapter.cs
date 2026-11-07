using System;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Services.Clients.OpenAI;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    /// <summary>
    /// OpenAI chat-completions wire adapter for ModelService endpoints.
    /// Delegates directly to Core's <see cref="OpenAIRequestSerializer"/> so request serialization
    /// and response parsing stay 100% shared with Core's built-in client.
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

            return OpenAIRequestSerializer.BuildRequestJson(
                envelope,
                modelName,
                defaultMaxTokens: 0,
                stream: false,
                forceJsonMode: false,
                omitZeroMaxTokens: true);
        }

        public static Result<LlmResponse, RimMindError> ParseResponse(string responseJson)
            => OpenAIRequestSerializer.ParseResponse(responseJson, null);

        public static Result<LlmResponse, RimMindError> ParseResponse(string responseJson, LlmRequestEnvelope? envelope)
            => OpenAIRequestSerializer.ParseResponse(responseJson, envelope);
    }
}
