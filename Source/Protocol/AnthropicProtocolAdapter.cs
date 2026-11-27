using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Models;
using RimMind.ModelService.Security;

namespace RimMind.ModelService.Protocol
{
    public static class AnthropicProtocolAdapter
    {
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
            var root = new JObject
            {
                ["model"] = !string.IsNullOrEmpty(modelName) ? modelName! : "claude-3-5-haiku-20241022",
                ["max_tokens"] = envelope.MaxTokens > 0 ? envelope.MaxTokens : 1024,
                ["temperature"] = envelope.Temperature
            };

            // 1. Partition 4-Zone KV-cache layers via Core's shared PromptZonePartitioner so
            // static system prompts (L0/L1) stay top-level cache-stable while volatile observations
            // (L2/L3/L5) attach to the active user turn.
            var partition = PromptZonePartitioner.Partition(envelope);
            if (!string.IsNullOrEmpty(partition.CombinedStaticSystemPrompt))
            {
                root["system"] = partition.CombinedStaticSystemPrompt;
            }

            string? observationPrefix = partition.CombinedVolatileObservation;

            // 2. Format messages (few-shot examples precede the live conversation)
            var messagesArray = new JArray();
            if (envelope.Messages != null && envelope.Messages.Count > 0)
            {
                var lastUserMsgRef = partition.LastUserMessage;
                var nonSystemMessages = new List<ChatMessage>(partition.Examples.Count + partition.LiveNonSystemMessages.Count);
                nonSystemMessages.AddRange(partition.Examples);
                nonSystemMessages.AddRange(partition.LiveNonSystemMessages);

                JObject? lastUserMsg = null;
                foreach (var msg in nonSystemMessages)
                {
                    bool isAssistant = string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase);
                    string role = isAssistant ? "assistant" : "user";
                    var contentArr = new JArray();

                    // If it is a tool result, output tool_result block ONLY (do not duplicate text)
                    if (!string.IsNullOrEmpty(msg.ToolCallId))
                    {
                        contentArr.Add(new JObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = msg.ToolCallId,
                            ["content"] = msg.Content ?? ""
                        });
                    }
                    else if (!string.IsNullOrEmpty(msg.Content) || (msg == lastUserMsgRef && !string.IsNullOrEmpty(observationPrefix)))
                    {
                        string textContent = msg.Content ?? "";
                        if (msg == lastUserMsgRef && !string.IsNullOrEmpty(observationPrefix))
                        {
                            textContent = $"<observation>\n{observationPrefix}\n</observation>\n\n{textContent}".TrimEnd();
                        }

                        if (!string.IsNullOrEmpty(textContent))
                        {
                            contentArr.Add(new JObject
                            {
                                ["type"] = "text",
                                ["text"] = textContent
                            });
                        }
                    }

                    if (allowVisionAttachments &&
                        role == "user" &&
                        !isAssistant &&
                        msg.Attachments != null &&
                        msg.Attachments.Count > 0)
                    {
                        for (int a = 0; a < msg.Attachments.Count; a++)
                        {
                            var att = msg.Attachments[a];
                            if (att == null || string.IsNullOrWhiteSpace(att.Base64Data))
                                continue;

                            string mediaType = !string.IsNullOrWhiteSpace(att.MediaType)
                                ? att.MediaType.Trim()
                                : "image/png";

                            contentArr.Add(new JObject
                            {
                                ["type"] = "image",
                                ["source"] = new JObject
                                {
                                    ["type"] = "base64",
                                    ["media_type"] = mediaType,
                                    ["data"] = att.Base64Data.Trim()
                                }
                            });
                        }
                    }

                    if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        foreach (var tc in msg.ToolCalls)
                        {
                            JToken inputObj;
                            try
                            {
                                inputObj = JToken.Parse(tc.Arguments ?? "{}");
                            }
                            catch
                            {
                                inputObj = new JObject();
                            }

                            contentArr.Add(new JObject
                            {
                                ["type"] = "tool_use",
                                ["id"] = tc.Id ?? Guid.NewGuid().ToString("N"),
                                ["name"] = tc.Name?.Replace('.', '_') ?? string.Empty,
                                ["input"] = inputObj
                            });
                        }
                    }

                    // Claude requires alternating user/assistant roles.
                    // Merge consecutive user messages (e.g. multiple tool results) into the previous user message.
                    if (role == "user" && lastUserMsg != null)
                    {
                        var existingContent = (JArray)lastUserMsg["content"]!;
                        foreach (var block in contentArr)
                        {
                            existingContent.Add(block);
                        }
                    }
                    else
                    {
                        if (contentArr.Count == 0)
                        {
                            contentArr.Add(new JObject
                            {
                                ["type"] = "text",
                                ["text"] = " "
                            });
                        }

                        var msgObj = new JObject
                        {
                            ["role"] = role,
                            ["content"] = contentArr
                        };
                        messagesArray.Add(msgObj);
                        lastUserMsg = role == "user" ? msgObj : null;
                    }
                }
            }

            root["messages"] = messagesArray;

            // 3. Format tools with deterministic sorting and cache_control
            if (envelope.Tools != null && envelope.Tools.Count > 0)
            {
                var toolsArray = new JArray();
                var sortedTools = envelope.Tools.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
                for (int i = 0; i < sortedTools.Count; i++)
                {
                    var tool = sortedTools[i];
                    var toolObj = new JObject
                    {
                        // Anthropic tool names must match ^[a-zA-Z0-9_-]{1,64}$; Core's ToolRegistry
                        // resolves the underscore form back to the dotted tool id.
                        ["name"] = tool.Name?.Replace('.', '_') ?? "",
                        ["description"] = tool.Description ?? ""
                    };

                    if (!string.IsNullOrEmpty(tool.Parameters))
                    {
                        try
                        {
                            toolObj["input_schema"] = JToken.Parse(tool.Parameters!);
                        }
                        catch
                        {
                            toolObj["input_schema"] = new JObject { ["type"] = "object" };
                        }
                    }
                    else
                    {
                        toolObj["input_schema"] = new JObject { ["type"] = "object" };
                    }

                    // Add ephemeral cache breakpoint to the last tool to cache all preceding tools and system prompt
                    if (i == sortedTools.Count - 1)
                    {
                        toolObj["cache_control"] = new JObject { ["type"] = "ephemeral" };
                    }

                    toolsArray.Add(toolObj);
                }
                root["tools"] = toolsArray;

                // Mirror Core's tool_choice=required semantics ("any" = must call at least one tool).
                if (envelope.Tools.Any(t => t.ToolChoice == "required"))
                    root["tool_choice"] = new JObject { ["type"] = "any" };
            }

            return root.ToString(Formatting.None);
        }

        public static Result<LlmResponse, RimMindError> ParseResponse(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
                return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientTransient("Empty response body from Anthropic server."));

            try
            {
                var token = JToken.Parse(responseJson);

                if (token["type"]?.ToString() == "error" || token["error"] != null)
                {
                    string msg = token["error"]?["message"]?.ToString() ?? "Unknown Anthropic error";
                    return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientPermanent(SecretRedactor.ForMessage(msg)));
                }

                if (string.Equals(token["stop_reason"]?.ToString(), "refusal", StringComparison.OrdinalIgnoreCase))
                {
                    return Result<LlmResponse, RimMindError>.Err(
                        RimMindErrors.ContentFiltered("Anthropic model refused request (stop_reason=refusal)."));
                }

                var contentArray = token["content"] as JArray;
                var textBuilder = new StringBuilder();
                var reasoningBuilder = new StringBuilder();
                string? toolCallsJson = null;

                if (contentArray != null)
                {
                    var wireList = new List<object>();
                    foreach (var block in contentArray)
                    {
                        string? type = block["type"]?.ToString();
                        if (type == "text")
                        {
                            textBuilder.Append(block["text"]?.ToString());
                        }
                        else if (type == "thinking")
                        {
                            reasoningBuilder.Append(block["thinking"]?.ToString());
                        }
                        else if (type == "tool_use")
                        {
                            string id = block["id"]?.ToString() ?? Guid.NewGuid().ToString("N");
                            string name = block["name"]?.ToString() ?? "";
                            var input = block["input"];
                            string argsJson = input != null ? input.ToString(Formatting.None) : "{}";

                            wireList.Add(new
                            {
                                id = id,
                                type = "function",
                                function = new
                                {
                                    name = name,
                                    arguments = argsJson
                                }
                            });
                        }
                    }
                    if (wireList.Count > 0)
                    {
                        toolCallsJson = JsonConvert.SerializeObject(wireList);
                    }
                }

                int inputTokens = token["usage"]?["input_tokens"]?.Value<int>() ?? 0;
                int outputTokens = token["usage"]?["output_tokens"]?.Value<int>() ?? 0;
                int cacheReadTokens = token["usage"]?["cache_read_input_tokens"]?.Value<int>() ?? 0;

                var response = new LlmResponse
                {
                    RequestId = token["id"]?.ToString() ?? Guid.NewGuid().ToString("N"),
                    Content = textBuilder.ToString(),
                    ReasoningContent = reasoningBuilder.Length > 0 ? reasoningBuilder.ToString() : null,
                    ToolCallsJson = toolCallsJson,
                    PromptTokens = inputTokens,
                    CompletionTokens = outputTokens,
                    CachedTokens = cacheReadTokens,
                    TokensUsed = inputTokens + outputTokens
                };

                return Result<LlmResponse, RimMindError>.Ok(response);
            }
            catch (Exception ex)
            {
                return Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.ClientPermanent($"Failed to parse Anthropic response: {SecretRedactor.ForMessage(ex.Message)}"));
            }
        }
    }
}
