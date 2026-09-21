using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Balancing;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;
using RimMind.ModelService.Security;
using RimMind.ModelService.Settings;
using RimMind.Testing;
using Xunit;

namespace RimMind.ModelService.Tests.Contracts
{
    public class ModelServiceContracts
    {
        [Fact]
        public void LocalLoopbackValidator_safeguards_local_subscription_gateways()
        {
            ContractCaseRunner.Run(
                ("local loopback IP is permitted", () =>
                {
                    Assert.True(LocalLoopbackValidator.IsLoopbackAddress("http://127.0.0.1:8000/v1"));
                    Assert.True(LocalLoopbackValidator.IsLoopbackAddress("127.0.0.1:11434"));
                }),
                ("localhost hostname is permitted", () =>
                {
                    Assert.True(LocalLoopbackValidator.IsLoopbackAddress("http://localhost:3000"));
                    Assert.True(LocalLoopbackValidator.IsLoopbackAddress("https://localhost/v1"));
                }),
                ("ipv6 loopback is permitted", () =>
                {
                    Assert.True(LocalLoopbackValidator.IsLoopbackAddress("http://[::1]:8080"));
                }),
                ("external public IP and hostnames are rejected", () =>
                {
                    Assert.False(LocalLoopbackValidator.IsLoopbackAddress("https://api.openai.com/v1"));
                    Assert.False(LocalLoopbackValidator.IsLoopbackAddress("http://192.168.1.100:8000"));
                    Assert.False(LocalLoopbackValidator.IsLoopbackAddress("http://8.8.8.8:80"));
                }),
                ("null and empty strings are rejected", () =>
                {
                    Assert.False(LocalLoopbackValidator.IsLoopbackAddress(null));
                    Assert.False(LocalLoopbackValidator.IsLoopbackAddress(""));
                    Assert.False(LocalLoopbackValidator.IsLoopbackAddress("   "));
                }));
        }

        [Fact]
        public void LoadBalancer_priority_failover_and_circuit_breaker_isolation()
        {
            ContractCaseRunner.Run(
                ("primary node is selected first based on lowest priority number", () =>
                {
                    var primary = new ModelEndpointConfig { id = "node-primary", priority = 0, isEnabled = true };
                    var backup = new ModelEndpointConfig { id = "node-backup", priority = 1, isEnabled = true };
                    var balancer = new LoadBalancer();

                    var selected = balancer.SelectEndpoint(new[] { primary, backup }, BalancingStrategy.PriorityFailover, 100);
                    Assert.Same(primary, selected);
                }),
                ("circuit breaker isolates failing node and fails over to backup", () =>
                {
                    var primary = new ModelEndpointConfig { id = "node-primary", priority = 0, isEnabled = true };
                    var backup = new ModelEndpointConfig { id = "node-backup", priority = 1, isEnabled = true };
                    var balancer = new LoadBalancer();

                    // Primary experiences 3 consecutive failures
                    primary.RecordFailure(currentTick: 1000, cooldownTicks: 500);
                    primary.RecordFailure(currentTick: 1001, cooldownTicks: 500);
                    primary.RecordFailure(currentTick: 1002, cooldownTicks: 500);

                    Assert.True(primary.IsTemporarilyIsolated(currentTick: 1200));

                    // At tick 1200, primary is isolated, load balancer must select backup
                    var selected = balancer.SelectEndpoint(new[] { primary, backup }, BalancingStrategy.PriorityFailover, 1200);
                    Assert.Same(backup, selected);

                    // At tick 1600 (past cooldown), primary is no longer isolated and can recover
                    Assert.False(primary.IsTemporarilyIsolated(currentTick: 1600));
                    var recovered = balancer.SelectEndpoint(new[] { primary, backup }, BalancingStrategy.PriorityFailover, 1600);
                    Assert.Same(primary, recovered);
                }));
        }

        [Fact]
        public void LoadBalancer_round_robin_distributes_requests_smoothly()
        {
            ContractCaseRunner.Run(
                ("round robin rotates evenly among eligible nodes", () =>
                {
                    var n1 = new ModelEndpointConfig { id = "n1", isEnabled = true };
                    var n2 = new ModelEndpointConfig { id = "n2", isEnabled = true };
                    var n3 = new ModelEndpointConfig { id = "n3", isEnabled = true };
                    var balancer = new LoadBalancer();

                    var nodes = new[] { n1, n2, n3 };
                    var picks = new List<string>();
                    for (int i = 0; i < 6; i++)
                    {
                        var pick = balancer.SelectEndpoint(nodes, BalancingStrategy.RoundRobin, 100);
                        Assert.NotNull(pick);
                        picks.Add(pick!.id);
                    }

                    Assert.Equal(2, picks.Count(id => id == "n1"));
                    Assert.Equal(2, picks.Count(id => id == "n2"));
                    Assert.Equal(2, picks.Count(id => id == "n3"));
                }));
        }

        [Fact]
        public void OpenAIProtocolAdapter_serializes_and_parses_faithfully()
        {
            ContractCaseRunner.Run(
                ("request json includes system prompt, user prompt, and tool definitions", () =>
                {
                    var envelope = new LlmRequestEnvelope
                    {
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", Content = "Be a helpful RimWorld assistant." },
                            new ChatMessage { Role = "user", Content = "Check power status." }
                        },
                        Temperature = 0.5f,
                        MaxTokens = 400
                    };
                    var node = new ModelEndpointConfig { modelName = "deepseek-chat" };

                    string json = OpenAIProtocolAdapter.BuildRequestJson(envelope, node);
                    Assert.Contains("deepseek-chat", json);
                    Assert.Contains("helpful RimWorld assistant", json);
                    Assert.Contains("Check power status", json);
                }),
                ("parse response extracts message content, reasoning, and token usage", () =>
                {
                    string rawResponse = @"{
                        ""id"": ""chatcmpl-test1234"",
                        ""choices"": [{
                            ""message"": {
                                ""role"": ""assistant"",
                                ""content"": ""Power grid is stable."",
                                ""reasoning_content"": ""Generator has 500 fuel.""
                            }
                        }],
                        ""usage"": {
                            ""prompt_tokens"": 45,
                            ""completion_tokens"": 15,
                            ""total_tokens"": 60
                        }
                    }";

                    var result = OpenAIProtocolAdapter.ParseResponse(rawResponse);
                    Assert.True(result.IsOk);
                    var response = result.Value;
                    Assert.Equal("Power grid is stable.", response.Content);
                    Assert.Equal("Generator has 500 fuel.", response.ReasoningContent);
                    Assert.Equal(60, response.TokensUsed);
                }));
        }

        [Fact]
        public void AnthropicProtocolAdapter_separates_system_and_maps_tools()
        {
            ContractCaseRunner.Run(
                ("system prompt is extracted into top-level property and messages array only keeps non-system", () =>
                {
                    var envelope = new LlmRequestEnvelope
                    {
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", Content = "System rules" },
                            new ChatMessage { Role = "user", Content = "Hello" }
                        },
                        Temperature = 0.7f,
                        MaxTokens = 1000
                    };
                    var node = new ModelEndpointConfig { modelName = "claude-3-5-sonnet-20241022" };

                    string json = AnthropicProtocolAdapter.BuildRequestJson(envelope, node);
                    Assert.Contains("\"system\":", json);
                    Assert.Contains("claude-3-5-sonnet-20241022", json);
                    Assert.Contains("Hello", json);
                }),
                ("parse response handles text and tool_use blocks", () =>
                {
                    string anthropicResponse = @"{
                        ""id"": ""msg_test5678"",
                        ""type"": ""message"",
                        ""role"": ""assistant"",
                        ""content"": [
                            { ""type"": ""text"", ""text"": ""Tending wounded pawn."" },
                            { ""type"": ""tool_use"", ""id"": ""toolu_01"", ""name"": ""actions.triage_patient"", ""input"": { ""pawn_id"": 10 } }
                        ],
                        ""usage"": {
                            ""input_tokens"": 120,
                            ""output_tokens"": 30
                        }
                    }";

                    var result = AnthropicProtocolAdapter.ParseResponse(anthropicResponse);
                    Assert.True(result.IsOk);
                    var response = result.Value;
                    Assert.Equal("Tending wounded pawn.", response.Content);
                    Assert.Contains("actions.triage_patient", response.ToolCallsJson);
                    Assert.Equal(150, response.TokensUsed);
                }));
        }

        [Fact]
        public void ModelEndpointPresets_creates_valid_codex_and_opencode_presets()
        {
            ContractCaseRunner.Run(
                ("Codex preset has valid configuration and satisfies loopback security", () =>
                {
                    var codex = ModelEndpointPresets.CreateCodexGatewayPreset(0);
                    Assert.Equal("Codex Local Gateway (sub2api)", codex.name);
                    Assert.Equal("http://127.0.0.1:8000/v1", codex.endpoint);
                    Assert.Equal("gpt-4o", codex.modelName);
                    Assert.Equal(ProviderType.LocalSubscriptionGateway, codex.providerType);
                    Assert.True(codex.isEnabled);
                    Assert.Equal(0, codex.priority);
                    Assert.True(LocalLoopbackValidator.IsLoopbackAddress(codex.endpoint));
                }),
                ("OpenCode Go preset has valid configuration and satisfies cloud endpoint routing", () =>
                {
                    var openCode = ModelEndpointPresets.CreateOpenCodeGoPreset(1);
                    Assert.Equal("OpenCode Go (Direct)", openCode.name);
                    Assert.Equal("https://opencode.ai/zen/go/v1", openCode.endpoint);
                    Assert.Equal("deepseek-v4.1-flash", openCode.modelName);
                    Assert.Equal(ProviderType.OpenCodeGo, openCode.providerType);
                    Assert.True(openCode.isEnabled);
                    Assert.Equal(1, openCode.priority);
                    Assert.False(openCode.IsLoopbackAddress);
                }),
                ("ModelEndpointConfig convenience static factories match preset factories", () =>
                {
                    var c1 = ModelEndpointConfig.CreateCodexGatewayPreset(2);
                    Assert.Equal("Codex Local Gateway (sub2api)", c1.name);
                    Assert.Equal(2, c1.priority);

                    var o1 = ModelEndpointConfig.CreateOpenCodeGoPreset(3);
                    Assert.Equal("OpenCode Go (Direct)", o1.name);
                    Assert.Equal(3, o1.priority);
                }));
        }

        [Fact]
        public void ModelServiceSettings_seeds_default_presets_when_initialized_or_reset()
        {
            ContractCaseRunner.Run(
                ("new settings instance is seeded with the two default presets", () =>
                {
                    var settings = new ModelServiceSettings();
                    Assert.NotNull(settings.endpoints);
                    Assert.Equal(2, settings.endpoints.Count);

                    var codex = settings.endpoints[0];
                    Assert.Equal("Codex Local Gateway (sub2api)", codex.name);
                    Assert.Equal("http://127.0.0.1:8000/v1", codex.endpoint);
                    Assert.Equal(ProviderType.LocalSubscriptionGateway, codex.providerType);

                    var openCode = settings.endpoints[1];
                    Assert.Equal("OpenCode Go (Direct)", openCode.name);
                    Assert.Equal("https://opencode.ai/zen/go/v1", openCode.endpoint);
                    Assert.Equal(ProviderType.OpenCodeGo, openCode.providerType);
                }),
                ("ResetToDefault clears and restores default presets", () =>
                {
                    var settings = new ModelServiceSettings();
                    settings.endpoints.Clear();
                    settings.endpoints.Add(new ModelEndpointConfig { name = "Custom" });
                    Assert.Single(settings.endpoints);

                    settings.ResetToDefault();
                    Assert.Equal(2, settings.endpoints.Count);
                    Assert.Equal("Codex Local Gateway (sub2api)", settings.endpoints[0].name);
                    Assert.Equal("OpenCode Go (Direct)", settings.endpoints[1].name);
                }),
                ("EnsureDefaultEndpoints seeds presets when list is empty", () =>
                {
                    var settings = new ModelServiceSettings();
                    settings.endpoints.Clear();
                    Assert.Empty(settings.endpoints);

                    settings.EnsureDefaultEndpoints();
                    Assert.Equal(2, settings.endpoints.Count);
                }));
        }

        [Fact]
        public void AnthropicProtocolAdapter_aggregates_consecutive_tool_results_into_single_user_message()
        {
            ContractCaseRunner.Run(
                ("multiple tool results are merged into one user message with multiple tool_result blocks", () =>
                {
                    var envelope = new LlmRequestEnvelope
                    {
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "assistant", Content = "Calling tools" },
                            new ChatMessage { Role = "tool", ToolCallId = "call_1", Content = "Result 1" },
                            new ChatMessage { Role = "tool", ToolCallId = "call_2", Content = "Result 2" }
                        }
                    };
                    var node = ModelEndpointPresets.CreateOpenCodeGoPreset();

                    string json = AnthropicProtocolAdapter.BuildRequestJson(envelope, node);
                    var parsed = Newtonsoft.Json.Linq.JObject.Parse(json);
                    var messages = (Newtonsoft.Json.Linq.JArray)parsed["messages"]!;

                    // Should have exactly 2 messages: 1 assistant, 1 user (with both tool results)
                    Assert.Equal(2, messages.Count);
                    Assert.Equal("assistant", messages[0]["role"]?.ToString());
                    Assert.Equal("user", messages[1]["role"]?.ToString());

                    var contentBlocks = (Newtonsoft.Json.Linq.JArray)messages[1]["content"]!;
                    Assert.Equal(2, contentBlocks.Count);
                    Assert.Equal("tool_result", contentBlocks[0]["type"]?.ToString());
                    Assert.Equal("call_1", contentBlocks[0]["tool_use_id"]?.ToString());
                    Assert.Equal("tool_result", contentBlocks[1]["type"]?.ToString());
                    Assert.Equal("call_2", contentBlocks[1]["tool_use_id"]?.ToString());
                }));
        }

        [Fact]
        public void ProtocolAdapters_serialize_tool_calls_with_nested_function_schema()
        {
            ContractCaseRunner.Run(
                ("OpenAI protocol tool calls serialize with type=function and function={name, arguments}", () =>
                {
                    string rawResponse = @"{
                        ""choices"": [{
                            ""message"": {
                                ""content"": null,
                                ""tool_calls"": [{
                                    ""id"": ""call_abc123"",
                                    ""type"": ""function"",
                                    ""function"": {
                                        ""name"": ""actions.emergency_flee"",
                                        ""arguments"": ""{\""danger_level\"":2}""
                                    }
                                }]
                            }
                        }]
                    }";

                    var res = OpenAIProtocolAdapter.ParseResponse(rawResponse);
                    Assert.True(res.IsOk);
                    Assert.NotNull(res.Value.ToolCallsJson);

                    var parsedCalls = Newtonsoft.Json.Linq.JArray.Parse(res.Value.ToolCallsJson!);
                    Assert.Single(parsedCalls);
                    Assert.Equal("call_abc123", parsedCalls[0]["id"]?.ToString());
                    Assert.Equal("function", parsedCalls[0]["type"]?.ToString());
                    Assert.NotNull(parsedCalls[0]["function"]);
                    Assert.Equal("actions.emergency_flee", parsedCalls[0]["function"]?["name"]?.ToString());
                    Assert.Equal("{\"danger_level\":2}", parsedCalls[0]["function"]?["arguments"]?.ToString());
                }),
                ("Anthropic protocol tool calls serialize with type=function and function={name, arguments}", () =>
                {
                    string rawResponse = @"{
                        ""content"": [{
                            ""type"": ""tool_use"",
                            ""id"": ""toolu_xyz456"",
                            ""name"": ""actions.triage_patient"",
                            ""input"": { ""pawn_id"": 42 }
                        }]
                    }";

                    var res = AnthropicProtocolAdapter.ParseResponse(rawResponse);
                    Assert.True(res.IsOk);
                    Assert.NotNull(res.Value.ToolCallsJson);

                    var parsedCalls = Newtonsoft.Json.Linq.JArray.Parse(res.Value.ToolCallsJson!);
                    Assert.Single(parsedCalls);
                    Assert.Equal("toolu_xyz456", parsedCalls[0]["id"]?.ToString());
                    Assert.Equal("function", parsedCalls[0]["type"]?.ToString());
                    Assert.NotNull(parsedCalls[0]["function"]);
                    Assert.Equal("actions.triage_patient", parsedCalls[0]["function"]?["name"]?.ToString());
                    Assert.Contains("42", parsedCalls[0]["function"]?["arguments"]?.ToString());
                }));
        }

        [Fact]
        public async System.Threading.Tasks.Task ModelServiceClient_rejects_non_loopback_local_gateway_permanently()
        {
            var rogueNode = new ModelEndpointConfig
            {
                name = "Rogue Node",
                endpoint = "https://malicious-external-site.com/v1",
                providerType = ProviderType.LocalSubscriptionGateway,
                isEnabled = true
            };

            using var client = new RimMind.ModelService.Client.ModelServiceClient(
                () => new List<ModelEndpointConfig> { rogueNode },
                () => BalancingStrategy.PriorityFailover);

            var envelope = new LlmRequestEnvelope();
            var result = await client.SendAsync(envelope);

            Assert.True(result.IsErr);
            Assert.Equal(RimMindErrorCode.ClientPermanentFailure, result.Error.Code);
            Assert.Contains("Security violation", result.Error.Message);
        }

        [Fact]
        public void ModelServiceSettings_endpoints_reordering_and_priority_sync()
        {
            ContractCaseRunner.Run(
                ("EnsureDefaultEndpoints seeds Codex and OpenCode Go presets in order", () =>
                {
                    var settings = new ModelServiceSettings();
                    settings.endpoints.Clear();
                    settings.EnsureDefaultEndpoints();

                    Assert.Equal(2, settings.endpoints.Count);
                    Assert.Equal(0, settings.endpoints[0].priority);
                    Assert.Equal(1, settings.endpoints[1].priority);
                    Assert.Contains("Codex", settings.endpoints[0].name);
                    Assert.Contains("OpenCode Go", settings.endpoints[1].name);
                }),
                ("reordering endpoints updates priorities deterministically", () =>
                {
                    var settings = new ModelServiceSettings();
                    settings.endpoints.Clear();
                    settings.EnsureDefaultEndpoints();

                    // Swap index 1 to 0 (Move Up)
                    var temp = settings.endpoints[1];
                    settings.endpoints[1] = settings.endpoints[0];
                    settings.endpoints[0] = temp;
                    for (int i = 0; i < settings.endpoints.Count; i++)
                        settings.endpoints[i].priority = i;

                    Assert.Contains("OpenCode Go", settings.endpoints[0].name);
                    Assert.Equal(0, settings.endpoints[0].priority);
                    Assert.Contains("Codex", settings.endpoints[1].name);
                    Assert.Equal(1, settings.endpoints[1].priority);

                    // Delete index 0 leaves remaining item with priority 0
                    settings.endpoints.RemoveAt(0);
                    for (int i = 0; i < settings.endpoints.Count; i++)
                        settings.endpoints[i].priority = i;

                    Assert.Single(settings.endpoints);
                    Assert.Contains("Codex", settings.endpoints[0].name);
                    Assert.Equal(0, settings.endpoints[0].priority);
                }));
        }

        [Fact]
        public void ModelEndpointConfig_OpenCodeGo_is_configured_and_recognized()
        {
            var config = new ModelEndpointConfig
            {
                name = "OpenCode Go Direct",
                endpoint = "https://opencode.ai/zen/go/v1",
                modelName = "deepseek-v4.1-flash",
                providerType = ProviderType.OpenCodeGo,
                apiKey = "oc_sk_test",
                isEnabled = true
            };

            Assert.True(config.isEnabled);
            Assert.False(config.IsLoopbackAddress);
            Assert.Equal(ProviderType.OpenCodeGo, config.providerType);
        }

        [Fact]
        public async System.Threading.Tasks.Task EndpointHealthProbe_probes_OpenCodeGo_if_key_provided()
        {
            string? testKey = Environment.GetEnvironmentVariable("RIMMIND_TEST_API_KEY");
            if (string.IsNullOrEmpty(testKey)) return;

            var config = new ModelEndpointConfig
            {
                name = "OpenCode Go Direct",
                endpoint = "https://opencode.ai/zen/go/v1",
                modelName = "deepseek-v4.1-flash",
                providerType = ProviderType.OpenCodeGo,
                apiKey = testKey,
                isEnabled = true
            };

            var (success, latency, msg) = await RimMind.ModelService.Diagnostics.EndpointHealthProbe.ProbeEndpointAsync(config);
            Assert.True(success, $"Probe failed with message: {msg}");
            Assert.True(latency > 0, "Latency should be positive");
        }
    }
}
