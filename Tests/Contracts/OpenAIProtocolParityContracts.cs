using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RimMind.Domain.Llm;
using RimMind.Infrastructure.Services.Clients.OpenAI;
using RimMind.Infrastructure.Services.Clients.Shared;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;
using RimMind.ModelService.Security;
using RimMind.Testing;
using Xunit;

namespace RimMind.ModelService.Tests.Contracts
{
    /// <summary>
    /// Golden-JSON parity between ModelService's OpenAI adapter and Core's built-in serializer.
    /// Core's internal <c>OpenAIRequestSerializer</c> and <c>LogSanitizer</c> are compiled into this
    /// test assembly (test-only source links) so both outputs are compared byte-for-byte.
    /// </summary>
    public class OpenAIProtocolParityContracts
    {
        private const string Model = "test-model";

        private const string GoldenToolLoopRequest =
            "{\"model\":\"test-model\",\"messages\":[" +
            "{\"role\":\"system\",\"content\":\"S\"}," +
            "{\"role\":\"user\",\"content\":\"EU\"}," +
            "{\"role\":\"assistant\",\"content\":\"EA\"}," +
            "{\"role\":\"user\",\"content\":\"U1\"}," +
            "{\"role\":\"assistant\",\"content\":\"\",\"reasoning_content\":\"R\",\"tool_calls\":[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"actions_move_pawn\",\"arguments\":\"{\\\"x\\\":1}\"}}]}," +
            "{\"role\":\"tool\",\"content\":\"ok\",\"tool_call_id\":\"call_1\"}]," +
            "\"max_tokens\":256,\"temperature\":0.5,\"stream\":false," +
            "\"response_format\":{\"type\":\"json_schema\",\"json_schema\":{\"name\":\"response\",\"schema\":{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\"}}}}}," +
            "\"tools\":[" +
            "{\"type\":\"function\",\"function\":{\"name\":\"actions_move_pawn\",\"description\":\"Move\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}}," +
            "{\"type\":\"function\",\"function\":{\"name\":\"core_query\",\"description\":\"Q\",\"parameters\":{\"type\":\"object\"}}}]," +
            "\"tool_choice\":\"required\"}";

        private static ModelEndpointConfig Node() => new ModelEndpointConfig { modelName = Model };

        private static LlmRequestEnvelope ToolLoopEnvelope() => new LlmRequestEnvelope
        {
            RequestId = "req-parity",
            MaxTokens = 256,
            Temperature = 0.5f,
            JsonSchema = "{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\"}}}",
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = "S" },
                new ChatMessage { Role = "user", Content = "U1" },
                new ChatMessage
                {
                    Role = "assistant",
                    Content = "",
                    ReasoningContent = "R",
                    ToolCalls = new List<ChatToolCall>
                    {
                        new ChatToolCall { Id = "call_1", Name = "actions_move_pawn", Arguments = "{\"x\":1}" }
                    }
                },
                new ChatMessage { Role = "tool", Content = "ok", ToolCallId = "call_1" },
            },
            Examples = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "EU" },
                new ChatMessage { Role = "assistant", Content = "EA" },
            },
            Tools = new List<StructuredTool>
            {
                new StructuredTool { Name = "core.query", Description = "Q", Parameters = "{\"type\":\"object\"}" },
                new StructuredTool { Name = "actions.move_pawn", Description = "Move", Parameters = null, ToolChoice = "required" },
            },
        };

        private static string Core(LlmRequestEnvelope envelope) =>
            OpenAIRequestSerializer.BuildRequestJson(envelope, Model, defaultMaxTokens: 0);

        [Fact]
        public void OpenAI_request_wire_format_matches_core_serializer()
        {
            ContractCaseRunner.Run(
                ("tool loop with dotted tools, schema, examples and tool_choice=required is byte-identical to Core and the golden JSON", () =>
                {
                    var envelope = ToolLoopEnvelope();
                    string adapter = OpenAIProtocolAdapter.BuildRequestJson(envelope, Node());

                    Assert.Equal(GoldenToolLoopRequest, Core(envelope));
                    Assert.Equal(GoldenToolLoopRequest, adapter);
                }),
                ("text sentinel schema emits no response_format and tool_choice falls back to auto, same as Core", () =>
                {
                    var envelope = new LlmRequestEnvelope
                    {
                        MaxTokens = 120,
                        Temperature = 0.25f,
                        JsonSchema = "<Action>...</Action>",
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "hi" } },
                        Tools = new List<StructuredTool>
                        {
                            new StructuredTool { Name = "dialogue.reply", Description = "Reply", Parameters = "{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\"}}}" },
                        },
                    };

                    string adapter = OpenAIProtocolAdapter.BuildRequestJson(envelope, Node());
                    Assert.Equal(Core(envelope), adapter);

                    var parsed = JObject.Parse(adapter);
                    Assert.Null(parsed["response_format"]);
                    Assert.Equal("auto", parsed["tool_choice"]?.ToString());
                    Assert.Equal("dialogue_reply", parsed["tools"]?[0]?["function"]?["name"]?.ToString());
                }),
                ("plain chat without tools matches Core and carries no tools or tool_choice", () =>
                {
                    var envelope = new LlmRequestEnvelope
                    {
                        MaxTokens = 64,
                        Temperature = 0.7f,
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", Content = "rules" },
                            new ChatMessage { Role = "user", Content = "status?" },
                        },
                    };

                    string adapter = OpenAIProtocolAdapter.BuildRequestJson(envelope, Node());
                    Assert.Equal(Core(envelope), adapter);

                    var parsed = JObject.Parse(adapter);
                    Assert.Null(parsed["tools"]);
                    Assert.Null(parsed["tool_choice"]);
                }),
                ("without a token budget only max_tokens differs: ModelService omits it instead of sending 0", () =>
                {
                    var envelope = new LlmRequestEnvelope
                    {
                        MaxTokens = 0,
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "x" } },
                    };

                    var adapter = JObject.Parse(OpenAIProtocolAdapter.BuildRequestJson(envelope, Node()));
                    var core = JObject.Parse(Core(envelope));
                    Assert.Null(adapter["max_tokens"]);
                    Assert.Equal(0, core["max_tokens"]?.Value<int>());
                    core.Remove("max_tokens");
                    Assert.True(JToken.DeepEquals(core, adapter), adapter.ToString());
                }),
                ("a malformed tool schema degrades to an empty object schema instead of failing the request", () =>
                {
                    var envelope = new LlmRequestEnvelope
                    {
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "x" } },
                        Tools = new List<StructuredTool> { new StructuredTool { Name = "bad.tool", Description = "b", Parameters = "{not json" } },
                    };

                    var parsed = JObject.Parse(OpenAIProtocolAdapter.BuildRequestJson(envelope, Node()));
                    Assert.Equal("bad_tool", parsed["tools"]?[0]?["function"]?["name"]?.ToString());
                    Assert.Equal("object", parsed["tools"]?[0]?["function"]?["parameters"]?["type"]?.ToString());
                }));
        }

        [Fact]
        public void OpenAI_response_parsing_matches_core_client_semantics()
        {
            ContractCaseRunner.Run(
                ("tool calls keep the wire (underscore) name for ToolRegistry and use Core's ToolCallsJson shape", () =>
                {
                    string raw = "{\"id\":\"chatcmpl-1\",\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{\"role\":\"assistant\",\"content\":null," +
                                 "\"tool_calls\":[{\"id\":\"call_9\",\"type\":\"function\",\"function\":{\"name\":\"actions_move_pawn\",\"arguments\":\"{\\\"x\\\":2}\"}}]}}]," +
                                 "\"usage\":{\"prompt_tokens\":30,\"completion_tokens\":5,\"total_tokens\":35,\"prompt_tokens_details\":{\"cached_tokens\":24}}}";

                    var result = OpenAIProtocolAdapter.ParseResponse(raw, ToolLoopEnvelope());
                    Assert.True(result.IsOk);
                    var calls = JArray.Parse(result.Value.ToolCallsJson!);
                    Assert.Single(calls);
                    Assert.Equal("call_9", calls[0]["id"]?.ToString());
                    Assert.Equal("function", calls[0]["type"]?.ToString());
                    Assert.Equal("actions_move_pawn", calls[0]["function"]?["name"]?.ToString());
                    Assert.Equal("{\"x\":2}", calls[0]["function"]?["arguments"]?.ToString());
                    Assert.Equal(string.Empty, result.Value.Content);
                    Assert.Equal(24, result.Value.CachedTokens);
                    Assert.Equal(35, result.Value.TokensUsed);
                }),
                ("no tool calls yields null ToolCallsJson (not an empty string)", () =>
                {
                    string raw = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"hello\"}}]}";
                    var result = OpenAIProtocolAdapter.ParseResponse(raw, null);
                    Assert.True(result.IsOk);
                    Assert.Null(result.Value.ToolCallsJson);
                    Assert.Null(result.Value.ReasoningContent);
                    Assert.Equal("hello", result.Value.Content);
                }),
                ("reasoning-only reply falls back to reasoning text when no tools were offered", () =>
                {
                    string raw = "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"\",\"reasoning_content\":\"{\\\"a\\\":1}\"}}]}";
                    var plain = new LlmRequestEnvelope { Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "q" } } };
                    var result = OpenAIProtocolAdapter.ParseResponse(raw, plain);
                    Assert.True(result.IsOk);
                    Assert.Equal("{\"a\":1}", result.Value.Content);
                }),
                ("reasoning fallback is suppressed when tools were offered or the reply was truncated", () =>
                {
                    string stop = "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"\",\"reasoning_content\":\"thinking\"}}]}";
                    var withTools = OpenAIProtocolAdapter.ParseResponse(stop, ToolLoopEnvelope());
                    Assert.Equal(string.Empty, withTools.Value.Content);

                    string length = "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"\",\"reasoning_content\":\"thinking\"}}]}";
                    var truncated = OpenAIProtocolAdapter.ParseResponse(length, new LlmRequestEnvelope());
                    Assert.Equal(string.Empty, truncated.Value.Content);
                    Assert.Equal("thinking", truncated.Value.ReasoningContent);
                }),
                ("provider error messages are redacted before they become RimMindErrors", () =>
                {
                    string raw = "{\"error\":{\"message\":\"Incorrect API key provided: sk-proj-abcdefghijklmnop. Authorization: Bearer abcdefghijkl\"}}";
                    var result = OpenAIProtocolAdapter.ParseResponse(raw, null);
                    Assert.True(result.IsErr);
                    Assert.DoesNotContain("sk-proj-abcdefghijklmnop", result.Error.Message);
                    Assert.DoesNotContain("abcdefghijkl", result.Error.Message);
                }));
        }

        [Fact]
        public void Anthropic_request_uses_provider_safe_tool_names_and_required_choice()
        {
            ContractCaseRunner.Run(
                ("dotted tool ids are sent in underscore form and required maps to tool_choice any", () =>
                {
                    var parsed = JObject.Parse(AnthropicProtocolAdapter.BuildRequestJson(ToolLoopEnvelope(), Node()));
                    var tools = (JArray)parsed["tools"]!;
                    Assert.Equal("actions_move_pawn", tools[0]["name"]?.ToString());
                    Assert.Equal("core_query", tools[1]["name"]?.ToString());
                    Assert.Equal("any", parsed["tool_choice"]?["type"]?.ToString());
                }),
                ("few-shot examples precede the live turns and the tool loop stays paired", () =>
                {
                    var parsed = JObject.Parse(AnthropicProtocolAdapter.BuildRequestJson(ToolLoopEnvelope(), Node()));
                    var messages = (JArray)parsed["messages"]!;
                    Assert.Equal("EU", messages[0]["content"]?[0]?["text"]?.ToString());
                    Assert.Equal("assistant", messages[1]["role"]?.ToString());
                    Assert.Equal("EA", messages[1]["content"]?[0]?["text"]?.ToString());
                    Assert.Equal("U1", messages[2]["content"]?[0]?["text"]?.ToString());
                    Assert.Equal("tool_use", messages[3]["content"]?[0]?["type"]?.ToString());
                    Assert.Equal("call_1", messages[4]["content"]?[0]?["tool_use_id"]?.ToString());
                }),
                ("no tool_choice is sent unless a tool requires it, and empty tool calls parse to null", () =>
                {
                    var envelope = new LlmRequestEnvelope
                    {
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "x" } },
                        Tools = new List<StructuredTool> { new StructuredTool { Name = "a.b", Description = "d" } },
                    };
                    var parsed = JObject.Parse(AnthropicProtocolAdapter.BuildRequestJson(envelope, Node()));
                    Assert.Null(parsed["tool_choice"]);

                    var response = AnthropicProtocolAdapter.ParseResponse("{\"content\":[{\"type\":\"text\",\"text\":\"hi\"}]}");
                    Assert.True(response.IsOk);
                    Assert.Null(response.Value.ToolCallsJson);
                }));
        }

        [Fact]
        public void SecretRedactor_matches_core_log_sanitizer()
        {
            var corpus = new[]
            {
                "Authorization: Bearer abcdefghijklmnop",
                "Basic dXNlcjpwYXNzd29yZA==",
                "{\"api_key\":\"plainvalue123\",\"model\":\"x\"}",
                "x-api-key=gatewaytoken42&foo=bar",
                "refresh_token: r3fr3sh",
                "password=hunter2;",
                "keys sk-proj-1234567890ab oc_sk_abcdefghijk sk-ant-api03-abcdefghij AIzaSyA1234567890abcdef gsk_abcdefghijklmn xai-abcdefghijkl",
                "monkey=banana token_count=12 tokenizer=cl100k",
                "",
                "nothing secret here",
            };

            ContractCaseRunner.Run(
                ("every corpus line redacts exactly like Core's LogSanitizer", () =>
                {
                    foreach (var line in corpus)
                        Assert.Equal(LogSanitizer.Redact(line), SecretRedactor.Redact(line));
                }),
                ("shapes the previous ModelService regex leaked are now masked", () =>
                {
                    Assert.DoesNotContain("plainvalue123", SecretRedactor.Redact(corpus[2]));
                    Assert.DoesNotContain("gatewaytoken42", SecretRedactor.Redact(corpus[3]));
                    Assert.DoesNotContain("dXNlcjpwYXNzd29yZA", SecretRedactor.Redact(corpus[1]));
                    Assert.DoesNotContain("gsk_abcdefghijklmn", SecretRedactor.Redact(corpus[6]));
                }),
                ("the configured node key is masked literally even when it matches no generic shape", () =>
                {
                    string message = SecretRedactor.ForMessageWithSecret("connect failed for http://gw/?k=Zq9Wm4", "Zq9Wm4");
                    Assert.DoesNotContain("Zq9Wm4", message);
                }),
                ("messages are truncated with Core's length marker after redaction", () =>
                {
                    string longText = new string('a', 600);
                    Assert.Equal(LogSanitizer.ForLog(longText), SecretRedactor.ForMessage(longText));
                }));
        }
    }
}
