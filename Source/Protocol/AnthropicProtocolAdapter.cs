using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    public static class AnthropicProtocolAdapter
    {
        public static string BuildRequestJson(LlmRequestEnvelope envelope, ModelEndpointConfig node)
        {
            var root = new JObject
            {
                ["model"] = !string.IsNullOrEmpty(node.modelName) ? node.modelName : "claude-3-5-haiku-20241022",
                ["max_tokens"] = envelope.MaxTokens > 0 ? envelope.MaxTokens : 1024,
                ["temperature"] = envelope.Temperature
            };

            // 1. Anthropic separates system prompts into a top-level string (aggregate multiple if present)
            var systemPrompts = envelope.Messages?
                .Where(m => m.Role == "system" && !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => m.Content)
                .ToList();

            if (systemPrompts != null && systemPrompts.Count > 0)
            {
                root["system"] = string.Join("\n\n", systemPrompts);
            }

            // 2. Format messages
            var messagesArray = new JArray();
            if (envelope.Messages != null && envelope.Messages.Count > 0)
            {
                JObject? lastUserMsg = null;
                foreach (var msg in envelope.Messages)
                {
                    if (msg.Role == "system") continue; // Extracted to top-level

                    string role = msg.Role == "assistant" ? "assistant" : "user";
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
                    else if (!string.IsNullOrEmpty(msg.Content))
                    {
                        contentArr.Add(new JObject
                        {
                            ["type"] = "text",
                            ["text"] = msg.Content
                        });
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
                                ["name"] = tc.Name,
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

            // 3. Format tools
            if (envelope.Tools != null && envelope.Tools.Count > 0)
            {
                var toolsArray = new JArray();
                foreach (var tool in envelope.Tools)
                {
                    var toolObj = new JObject
                    {
                        ["name"] = tool.Name,
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

                    toolsArray.Add(toolObj);
                }
                root["tools"] = toolsArray;
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
                    return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientPermanent(msg));
                }

                var contentArray = token["content"] as JArray;
                var textBuilder = new StringBuilder();
                var reasoningBuilder = new StringBuilder();
                string toolCallsJson = "";

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

                var response = new LlmResponse
                {
                    RequestId = token["id"]?.ToString() ?? Guid.NewGuid().ToString("N"),
                    Content = textBuilder.ToString(),
                    ReasoningContent = reasoningBuilder.Length > 0 ? reasoningBuilder.ToString() : null,
                    ToolCallsJson = toolCallsJson,
                    PromptTokens = inputTokens,
                    CompletionTokens = outputTokens,
                    TokensUsed = inputTokens + outputTokens
                };

                return Result<LlmResponse, RimMindError>.Ok(response);
            }
            catch (Exception ex)
            {
                return Result<LlmResponse, RimMindError>.Err(
                    RimMindErrors.ClientPermanent($"Failed to parse Anthropic response: {ex.Message}"));
            }
        }
    }
}
