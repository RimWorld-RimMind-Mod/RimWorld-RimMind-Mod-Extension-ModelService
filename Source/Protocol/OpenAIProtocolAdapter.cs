using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    public static class OpenAIProtocolAdapter
    {
        public static string BuildRequestJson(LlmRequestEnvelope envelope, ModelEndpointConfig node)
        {
            var root = new JObject
            {
                ["model"] = !string.IsNullOrEmpty(node.modelName) ? node.modelName : "gpt-4o-mini",
                ["temperature"] = envelope.Temperature,
            };

            if (envelope.MaxTokens > 0)
            {
                root["max_tokens"] = envelope.MaxTokens;
            }

            var messagesArray = new JArray();

            if (envelope.Messages != null && envelope.Messages.Count > 0)
            {
                foreach (var msg in envelope.Messages)
                {
                    var msgObj = new JObject
                    {
                        ["role"] = msg.Role,
                        ["content"] = msg.Content ?? ""
                    };

                    if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        var toolCallsArr = new JArray();
                        foreach (var tc in msg.ToolCalls)
                        {
                            toolCallsArr.Add(new JObject
                            {
                                ["id"] = tc.Id ?? Guid.NewGuid().ToString("N"),
                                ["type"] = "function",
                                ["function"] = new JObject
                                {
                                    ["name"] = tc.Name,
                                    ["arguments"] = tc.Arguments ?? "{}"
                                }
                            });
                        }
                        msgObj["tool_calls"] = toolCallsArr;
                    }

                    if (!string.IsNullOrEmpty(msg.ToolCallId))
                    {
                        msgObj["tool_call_id"] = msg.ToolCallId;
                    }

                    messagesArray.Add(msgObj);
                }
            }

            root["messages"] = messagesArray;

            if (envelope.Tools != null && envelope.Tools.Count > 0)
            {
                var toolsArray = new JArray();
                foreach (var tool in envelope.Tools)
                {
                    var funcObj = new JObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description ?? ""
                    };

                    if (!string.IsNullOrEmpty(tool.Parameters))
                    {
                        try
                        {
                            funcObj["parameters"] = JToken.Parse(tool.Parameters!);
                        }
                        catch
                        {
                            funcObj["parameters"] = new JObject { ["type"] = "object" };
                        }
                    }

                    toolsArray.Add(new JObject
                    {
                        ["type"] = "function",
                        ["function"] = funcObj
                    });
                }
                root["tools"] = toolsArray;
            }

            return root.ToString(Formatting.None);
        }

        public static Result<LlmResponse, RimMindError> ParseResponse(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
                return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientTransient("Empty response body from model server."));

            try
            {
                var token = JToken.Parse(responseJson);

                if (token["error"] != null)
                {
                    string errMsg = token["error"]?["message"]?.ToString() ?? "Unknown API error";
                    return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientPermanent(errMsg));
                }

                var choices = token["choices"] as JArray;
                if (choices == null || choices.Count == 0)
                {
                    return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientPermanent("Response choices array is missing or empty."));
                }

                var message = choices[0]["message"];
                if (message == null)
                {
                    return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientPermanent("First choice message is null."));
                }

                string content = message["content"]?.ToString() ?? "";
                string reasoningContent = message["reasoning_content"]?.ToString() ?? "";
                string toolCallsJson = "";

                var toolCalls = message["tool_calls"] as JArray;
                if (toolCalls != null && toolCalls.Count > 0)
                {
                    var wireList = new List<object>();
                    foreach (var tc in toolCalls)
                    {
                        string id = tc["id"]?.ToString() ?? Guid.NewGuid().ToString("N");
                        string name = tc["function"]?["name"]?.ToString() ?? "";
                        string arguments = tc["function"]?["arguments"]?.ToString() ?? "{}";
                        wireList.Add(new
                        {
                            id = id,
                            type = "function",
                            function = new
                            {
                                name = name,
                                arguments = arguments
                            }
                        });
                    }
                    toolCallsJson = JsonConvert.SerializeObject(wireList);
                }

                int promptTokens = token["usage"]?["prompt_tokens"]?.Value<int>() ?? 0;
                int completionTokens = token["usage"]?["completion_tokens"]?.Value<int>() ?? 0;
                int totalTokens = token["usage"]?["total_tokens"]?.Value<int>() ?? (promptTokens + completionTokens);

                var llmResponse = new LlmResponse
                {
                    RequestId = token["id"]?.ToString() ?? Guid.NewGuid().ToString("N"),
                    Content = content,
                    ReasoningContent = reasoningContent,
                    ToolCallsJson = toolCallsJson,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    TokensUsed = totalTokens
                };

                return Result<LlmResponse, RimMindError>.Ok(llmResponse);
            }
            catch (Exception ex)
            {
                return Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientPermanent($"Failed to parse OpenAI response: {ex.Message}"));
            }
        }
    }
}
