using System;
using System.Text;
using Newtonsoft.Json.Linq;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    /// <summary>
    /// Native DeepSeek v4 / 2026-11 chat-completions protocol dialect adapter:
    /// normalizes retired model names (deepseek-chat / deepseek-reasoner -> deepseek-flash),
    /// translates engine-agnostic <see cref="ReasoningEffort"/> into DeepSeek <c>thinking</c> + <c>reasoning_effort</c>,
    /// injects <c>safety_check</c> and sanitized <c>user_id</c>, resolves parameter conflicts,
    /// preserves assistant <c>reasoning_content</c> on multi-turn tool calls, and parses keep-alive/risk responses.
    /// Uses <see cref="OpenAIWireCodec"/>'s single-pass <c>customizePayload</c> hook to avoid double JSON round-trips.
    /// </summary>
    public static class DeepSeekProtocolAdapter
    {
        public const string DefaultModelName = "deepseek-flash";
        public const string ProModelName = "deepseek-v4-pro";

        public static string NormalizeModelName(string? rawModel, out bool legacyImpliedNonThinking)
        {
            legacyImpliedNonThinking = false;
            if (string.IsNullOrWhiteSpace(rawModel))
                return DefaultModelName;

            string trimmed = rawModel!.Trim();
            if (string.Equals(trimmed, "deepseek-chat", StringComparison.OrdinalIgnoreCase))
            {
                legacyImpliedNonThinking = true;
                return DefaultModelName;
            }

            if (string.Equals(trimmed, "deepseek-reasoner", StringComparison.OrdinalIgnoreCase))
            {
                return DefaultModelName;
            }

            return trimmed;
        }

        public static string BuildRequestJson(
            LlmRequestEnvelope envelope,
            ModelEndpointConfig? endpoint,
            string? modelOverride = null)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));

            string rawModel = !string.IsNullOrEmpty(modelOverride)
                ? modelOverride!
                : (endpoint?.modelName ?? string.Empty);
            string normalizedModel = NormalizeModelName(rawModel, out bool legacyImpliedNonThinking);

            ResolveThinkingControl(
                envelope.ReasoningEffort,
                endpoint?.thinkingPolicy ?? EndpointThinkingPolicy.FollowRequest,
                legacyImpliedNonThinking,
                out bool thinkingEnabled,
                out string? reasoningEffortWire);

            bool safetyHigh = endpoint != null && endpoint.safetyLevel == EndpointSafetyLevel.High;
            string? sanitizedUserId = SanitizeUserId(envelope.NpcId);
            bool allowVisionAttachments = !string.Equals(normalizedModel, ProModelName, StringComparison.OrdinalIgnoreCase);

            return OpenAIWireCodec.BuildRequestJson(
                envelope,
                normalizedModel,
                defaultMaxTokens: 0,
                stream: false,
                forceJsonMode: false,
                omitZeroMaxTokens: true,
                customizePayload: root => ApplyDeepSeekDialect(
                    root,
                    thinkingEnabled,
                    reasoningEffortWire,
                    safetyHigh,
                    sanitizedUserId),
                allowVisionAttachments: allowVisionAttachments);
        }

        private static void ApplyDeepSeekDialect(
            JObject root,
            bool thinkingEnabled,
            string? reasoningEffortWire,
            bool safetyHigh,
            string? sanitizedUserId)
        {
            root["thinking"] = new JObject
            {
                ["type"] = thinkingEnabled ? "enabled" : "disabled"
            };
            if (thinkingEnabled && !string.IsNullOrEmpty(reasoningEffortWire))
            {
                root["reasoning_effort"] = reasoningEffortWire;
            }

            if (safetyHigh)
            {
                root["safety_check"] = new JObject
                {
                    ["level"] = "high"
                };
            }

            if (!string.IsNullOrEmpty(sanitizedUserId))
            {
                root["user_id"] = sanitizedUserId;
            }

            bool omitSampling = thinkingEnabled || safetyHigh;
            if (omitSampling)
            {
                root.Remove("temperature");
                if (root["tool_choice"] is JValue toolChoiceVal &&
                    toolChoiceVal.Type == JTokenType.String &&
                    string.Equals(toolChoiceVal.ToString(), "required", StringComparison.OrdinalIgnoreCase))
                {
                    root["tool_choice"] = "auto";
                }
            }

            var messages = root["messages"] as JArray;
            if (root["response_format"] is JObject responseFormat)
            {
                string? rfType = responseFormat["type"]?.ToString();
                if (string.Equals(rfType, "json_schema", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rfType, "json_object", StringComparison.OrdinalIgnoreCase))
                {
                    root["response_format"] = new JObject { ["type"] = "json_object" };
                    if (messages != null)
                    {
                        OpenAIWireCodec.EnsureJsonKeyword(messages);
                    }
                }
            }

            bool hasTools = root["tools"] is JArray toolsArr && toolsArr.Count > 0;
            if (thinkingEnabled && messages != null)
            {
                for (int i = 0; i < messages.Count; i++)
                {
                    if (messages[i] is JObject msgObj &&
                        string.Equals(msgObj["role"]?.ToString(), "assistant", StringComparison.OrdinalIgnoreCase) &&
                        (hasTools || msgObj["tool_calls"] != null) &&
                        msgObj["reasoning_content"] == null)
                    {
                        msgObj["reasoning_content"] = string.Empty;
                    }
                }
            }
        }

        public static Result<LlmResponse, RimMindError> ParseResponse(
            string responseBody,
            LlmRequestEnvelope? envelope = null)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.ClientTransient("Empty response body from DeepSeek server."));

            return OpenAIWireCodec.ParseResponse(responseBody.Trim(), envelope);
        }

        private static void ResolveThinkingControl(
            ReasoningEffort requestedEffort,
            EndpointThinkingPolicy policy,
            bool legacyImpliedNonThinking,
            out bool thinkingEnabled,
            out string? reasoningEffortWire)
        {
            if (policy == EndpointThinkingPolicy.ForceDisabled)
            {
                thinkingEnabled = false;
                reasoningEffortWire = null;
                return;
            }

            if (policy == EndpointThinkingPolicy.ForceEnabled)
            {
                thinkingEnabled = true;
                reasoningEffortWire = MapEffortWhenThinkingEnabled(requestedEffort);
                return;
            }

            switch (requestedEffort)
            {
                case ReasoningEffort.None:
                    thinkingEnabled = false;
                    reasoningEffortWire = null;
                    break;
                case ReasoningEffort.Low:
                    thinkingEnabled = true;
                    reasoningEffortWire = "low";
                    break;
                case ReasoningEffort.Medium:
                case ReasoningEffort.High:
                    thinkingEnabled = true;
                    reasoningEffortWire = "high";
                    break;
                case ReasoningEffort.Max:
                    thinkingEnabled = true;
                    reasoningEffortWire = "max";
                    break;
                default:
                    thinkingEnabled = !legacyImpliedNonThinking;
                    reasoningEffortWire = null;
                    break;
            }
        }

        private static string? MapEffortWhenThinkingEnabled(ReasoningEffort effort) => effort switch
        {
            ReasoningEffort.Low => "low",
            ReasoningEffort.Medium => "high",
            ReasoningEffort.High => "high",
            ReasoningEffort.Max => "max",
            _ => null
        };

        internal static string? SanitizeUserId(string? rawId)
        {
            if (string.IsNullOrWhiteSpace(rawId))
                return null;

            var sb = new StringBuilder(Math.Min(64, rawId!.Length));
            for (int i = 0; i < rawId.Length && sb.Length < 64; i++)
            {
                char ch = rawId[i];
                bool allowed = (ch >= 'a' && ch <= 'z')
                    || (ch >= 'A' && ch <= 'Z')
                    || (ch >= '0' && ch <= '9')
                    || ch == '_'
                    || ch == '-'
                    || ch == ':';
                sb.Append(allowed ? ch : '_');
            }

            string result = sb.ToString().Trim('_');
            return result.Length > 0 ? result : null;
        }
    }
}
