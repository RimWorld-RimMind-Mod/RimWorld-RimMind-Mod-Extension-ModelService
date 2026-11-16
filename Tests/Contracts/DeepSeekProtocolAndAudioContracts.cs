using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Domain.Audio;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Audio;
using RimMind.ModelService.Client;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;
using RimMind.ModelService.Settings;
using RimMind.Testing;
using Xunit;

namespace RimMind.ModelService.Tests.Contracts
{
    public sealed class DeepSeekProtocolAndAudioContracts
    {
        private sealed class LocalSettingsStub : ISettingsProvider
        {
            public string ApiKey { get; set; } = "";
            public string ApiEndpoint { get; set; } = "";
            public string ModelName { get; set; } = "";
            public string Provider { get; set; } = "deepseek_native";
            public bool ForceJsonMode { get; set; } = true;
            public int MaxTokens { get; set; } = 800;
            public float DefaultTemperature { get; set; } = 0.7f;
            public bool DebugLogging { get; set; }
            public int MaxConcurrentRequests { get; set; } = 2;
            public int MaxRetryCount { get; set; } = 2;
            public int RequestTimeoutMs { get; set; } = 30000;
            public int RequestExpireTicks { get; set; } = 600;
            public int BehaviorHistoryMax { get; set; } = 10;
            public int QueueProcessInterval { get; set; } = 10;
            public int DefaultModCooldownTicks { get; set; } = 600;
            public float ActivityFrequencyScale { get; set; } = 1.0f;
            public bool AutoActivateColonistAgents { get; set; }
            public bool IsConfigured => !string.IsNullOrEmpty(ApiKey);
            public bool IsOpenAIConfigured() => !string.IsNullOrEmpty(ApiKey);
            public void Persist() { }
            public int AgentTickInterval => 600;
            public int ThinkCooldownTicks => 300;
            public int MaxToolCallDepth => 3;
            public string Player2RemoteUrl { get; set; } = "";
            public IReadOnlyList<RimMind.Application.Common.Models.Client.BackupEndpointNode>? BackupEndpoints => null;
            public int CircuitBreakerFailureThreshold => 5;
            public int CircuitBreakerOpenDurationSec => 60;
            public int ContextCalibrateInterval { get; set; } = 1200;
            public int ContextDiffLifetimeTicks { get; set; } = 600;
            public bool RequestOverlayEnabled { get; set; } = true;
            public float RequestOverlayX { get; set; } = 20f;
            public float RequestOverlayY { get; set; } = 20f;
            public float RequestOverlayW { get; set; } = 300f;
            public float RequestOverlayH { get; set; } = 200f;
            public bool ShowAgentProgressFloat { get; set; }
            public bool RequestOverlayAutoHideWhenEmpty { get; set; } = true;
            public bool EnableFloatingMentalMonitor { get; set; }
            public string CustomPawnPrompt { get; set; } = "";
            public string CustomMapPrompt { get; set; } = "";
            public RimMind.Domain.Enums.FlywheelAutoApplyMode AutoApplyMode { get; set; }
            public float AutoApplyConfidenceThreshold { get; set; } = 0.8f;
            public RimMind.Domain.Enums.AgentAutonomyLevel AutonomyLevel { get; set; }
            public bool ShouldApproveAction(RimMind.Domain.Enums.RiskLevel risk) => true;
            public IContextSettings Context => this;
            public float ContextBudget { get; set; } = 1000f;
            public int ContextBriefLimit => 200;
            public int EnvironmentScanRadius => 15;
            public int EnvironmentMaxItems => 10;
            public float ThreatThresholdHigh => 0.8f;
            public float ThreatThresholdMedium => 0.5f;
            public float ThreatThresholdLow => 0.2f;
            public int MaxCacheEntries => 100;
            public float MoodDiffThreshold => 0.1f;
            public float TemperatureDiffThreshold => 5f;
            public bool IncludeRace { get; set; } = true;
            public bool IncludeAge { get; set; } = true;
            public bool IncludeGender { get; set; } = true;
            public bool IncludeBackstory { get; set; } = true;
            public bool IncludeIdeology { get; set; }
            public bool IncludeTraits { get; set; } = true;
            public bool IncludeSkills { get; set; } = true;
            public int MinSkillLevel { get; set; }
            public bool IncludeHealth { get; set; } = true;
            public bool IncludeCapacities { get; set; } = true;
            public bool IncludeMood { get; set; } = true;
            public bool IncludeMoodThoughts { get; set; }
            public bool IncludeCurrentJob { get; set; } = true;
            public bool IncludeWorkPriorities { get; set; } = true;
            public bool IncludeEquipment { get; set; } = true;
            public bool IncludeInventory { get; set; }
            public bool IncludeLocation { get; set; }
            public bool IncludeRelations { get; set; } = true;
            public bool IncludeGenes { get; set; } = true;
            public bool IncludeSurroundings { get; set; }
            public bool IncludeCombatStatus { get; set; } = true;
            public bool IncludeGameTime { get; set; } = true;
            public bool IncludeColonistCount { get; set; } = true;
            public bool IncludeColonistNames { get; set; } = true;
            public bool IncludeWealth { get; set; }
            public bool IncludeFood { get; set; } = true;
            public bool IncludeSeason { get; set; } = true;
            public bool IncludeWeather { get; set; } = true;
            public bool IncludeThreats { get; set; } = true;
            public void ApplyPreset(RimMind.Domain.Enums.ContextPreset preset) { }
            public void ResetToDefault() { }
        }

        [Fact]
        public void DeepSeekProtocolAdapter_normalizes_models_thinking_safety_conflicts_and_tool_history()
        {
            ContractCaseRunner.Run(
                ("retired deepseek-chat normalizes to deepseek-flash with thinking disabled under Auto", () =>
                {
                    var node = new ModelEndpointConfig { modelName = "deepseek-chat", safetyLevel = EndpointSafetyLevel.Standard };
                    var env = new LlmRequestEnvelope
                    {
                        ReasoningEffort = ReasoningEffort.Auto,
                        Temperature = 0.8f,
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "hi" } }
                    };

                    var root = JObject.Parse(DeepSeekProtocolAdapter.BuildRequestJson(env, node));
                    Assert.Equal("deepseek-flash", root["model"]?.ToString());
                    Assert.Equal("disabled", root["thinking"]?["type"]?.ToString());
                    Assert.Null(root["reasoning_effort"]);
                    Assert.NotNull(root["temperature"]);
                    Assert.Null(root["safety_check"]);
                }),
                ("retired deepseek-reasoner normalizes to deepseek-flash with thinking enabled", () =>
                {
                    var node = new ModelEndpointConfig { modelName = "deepseek-reasoner" };
                    var env = new LlmRequestEnvelope
                    {
                        ReasoningEffort = ReasoningEffort.Auto,
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "plan" } }
                    };

                    var root = JObject.Parse(DeepSeekProtocolAdapter.BuildRequestJson(env, node));
                    Assert.Equal("deepseek-flash", root["model"]?.ToString());
                    Assert.Equal("enabled", root["thinking"]?["type"]?.ToString());
                    Assert.Null(root["temperature"]);
                }),
                ("ReasoningEffort levels and EndpointThinkingPolicy overrides emit expected wire fields", () =>
                {
                    var followNode = new ModelEndpointConfig { modelName = "deepseek-v4-pro", thinkingPolicy = EndpointThinkingPolicy.FollowRequest };
                    var baseMsg = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "say hi" } };

                    var noneJson = JObject.Parse(DeepSeekProtocolAdapter.BuildRequestJson(new LlmRequestEnvelope { ReasoningEffort = ReasoningEffort.None, Messages = baseMsg }, followNode));
                    Assert.Equal("disabled", noneJson["thinking"]?["type"]?.ToString());
                    Assert.Null(noneJson["reasoning_effort"]);
                    Assert.NotNull(noneJson["temperature"]);

                    var lowJson = JObject.Parse(DeepSeekProtocolAdapter.BuildRequestJson(new LlmRequestEnvelope { ReasoningEffort = ReasoningEffort.Low, Messages = baseMsg }, followNode));
                    Assert.Equal("enabled", lowJson["thinking"]?["type"]?.ToString());
                    Assert.Equal("low", lowJson["reasoning_effort"]?.ToString());
                    Assert.Null(lowJson["temperature"]);

                    var maxJson = JObject.Parse(DeepSeekProtocolAdapter.BuildRequestJson(new LlmRequestEnvelope { ReasoningEffort = ReasoningEffort.Max, Messages = baseMsg }, followNode));
                    Assert.Equal("enabled", maxJson["thinking"]?["type"]?.ToString());
                    Assert.Equal("max", maxJson["reasoning_effort"]?.ToString());

                    var forceOffNode = new ModelEndpointConfig { modelName = "deepseek-flash", thinkingPolicy = EndpointThinkingPolicy.ForceDisabled };
                    var forcedOff = JObject.Parse(DeepSeekProtocolAdapter.BuildRequestJson(new LlmRequestEnvelope { ReasoningEffort = ReasoningEffort.Max, Messages = baseMsg }, forceOffNode));
                    Assert.Equal("disabled", forcedOff["thinking"]?["type"]?.ToString());
                    Assert.Null(forcedOff["reasoning_effort"]);

                    var forceOnNode = new ModelEndpointConfig { modelName = "deepseek-flash", thinkingPolicy = EndpointThinkingPolicy.ForceEnabled };
                    var forcedOn = JObject.Parse(DeepSeekProtocolAdapter.BuildRequestJson(new LlmRequestEnvelope { ReasoningEffort = ReasoningEffort.None, Messages = baseMsg }, forceOnNode));
                    Assert.Equal("enabled", forcedOn["thinking"]?["type"]?.ToString());
                }),
                ("Safety High + JsonSchema + required tool_choice + multi-turn assistant tool history resolve cleanly", () =>
                {
                    var node = new ModelEndpointConfig
                    {
                        modelName = "deepseek-flash",
                        thinkingPolicy = EndpointThinkingPolicy.ForceEnabled,
                        safetyLevel = EndpointSafetyLevel.High
                    };
                    var env = new LlmRequestEnvelope
                    {
                        NpcId = "Pawn 01/Bob@Colony",
                        Temperature = 0.9f,
                        JsonSchema = "{\"type\":\"object\",\"properties\":{\"ok\":{\"type\":\"boolean\"}}}",
                        Tools = new List<StructuredTool>
                        {
                            new StructuredTool { Name = "colony.build", Description = "Build", Parameters = "{\"type\":\"object\"}", ToolChoice = "required" }
                        },
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", Content = "Follow colony rules." },
                            new ChatMessage { Role = "user", Content = "Check walls" },
                            new ChatMessage
                            {
                                Role = "assistant",
                                Content = "",
                                ReasoningContent = null,
                                ToolCalls = new List<ChatToolCall>
                                {
                                    new ChatToolCall { Id = "call_1", Name = "colony_build", Arguments = "{}" }
                                }
                            },
                            new ChatMessage { Role = "tool", ToolCallId = "call_1", Content = "ok" }
                        }
                    };

                    var root = JObject.Parse(DeepSeekProtocolAdapter.BuildRequestJson(env, node));
                    Assert.Equal("high", root["safety_check"]?["level"]?.ToString());
                    Assert.Equal("Pawn_01_Bob_Colony", root["user_id"]?.ToString());
                    Assert.Null(root["temperature"]);
                    Assert.Equal("auto", root["tool_choice"]?.ToString());
                    Assert.Equal("json_object", root["response_format"]?["type"]?.ToString());
                    Assert.Null(root["response_format"]?["json_schema"]);

                    var sysContent = root["messages"]?[0]?["content"]?.ToString() ?? "";
                    Assert.Contains("json", sysContent, StringComparison.OrdinalIgnoreCase);

                    var assistantMsg = root["messages"]?[2] as JObject;
                    Assert.NotNull(assistantMsg);
                    Assert.NotNull(assistantMsg!["reasoning_content"]);
                    Assert.Equal("", assistantMsg["reasoning_content"]!.ToString());
                }));
        }

        [Fact]
        public void DeepSeekProtocolAdapter_parses_keepalive_errors_moderation_and_cache_tokens()
        {
            ContractCaseRunner.Run(
                ("keep-alive leading whitespace trims cleanly and extracts prompt_cache_hit_tokens", () =>
                {
                    string raw = "\n\n   {\"id\":\"ds-1\",\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"hello\"}}],\"usage\":{\"prompt_tokens\":50,\"completion_tokens\":5,\"total_tokens\":55,\"prompt_cache_hit_tokens\":32}}\n";
                    var res = DeepSeekProtocolAdapter.ParseResponse(raw);
                    Assert.True(res.IsOk);
                    Assert.Equal("hello", res.Value.Content);
                    Assert.Equal(32, res.Value.CachedTokens);
                }),
                ("HTTP 200 embedded errors map Content Exists Risk vs server_error vs invalid_request", () =>
                {
                    var modRes = DeepSeekProtocolAdapter.ParseResponse("{\"error\":{\"message\":\"Content Exists Risk\",\"type\":\"invalid_request_error\"}}");
                    Assert.True(modRes.IsErr);
                    Assert.Equal(RimMindErrorCode.ContentFiltered, modRes.Error.Code);

                    var transientRes = DeepSeekProtocolAdapter.ParseResponse("{\"error\":{\"message\":\"upstream busy\",\"type\":\"server_error\"}}");
                    Assert.True(transientRes.IsErr);
                    Assert.Equal(RimMindErrorCode.ClientTransientFailure, transientRes.Error.Code);

                    var permRes = DeepSeekProtocolAdapter.ParseResponse("{\"error\":{\"message\":\"bad param\",\"type\":\"invalid_request_error\"}}");
                    Assert.True(permRes.IsErr);
                    Assert.Equal(RimMindErrorCode.ClientPermanentFailure, permRes.Error.Code);
                }),
                ("finish_reason content_filter, insufficient_system_resource, aborted, and whitespace trap map cleanly", () =>
                {
                    var cf = DeepSeekProtocolAdapter.ParseResponse("{\"choices\":[{\"finish_reason\":\"content_filter\",\"message\":{\"role\":\"assistant\",\"content\":\"\"}}]}");
                    Assert.True(cf.IsErr);
                    Assert.Equal(RimMindErrorCode.ContentFiltered, cf.Error.Code);

                    var isr = DeepSeekProtocolAdapter.ParseResponse("{\"choices\":[{\"finish_reason\":\"insufficient_system_resource\",\"message\":{\"role\":\"assistant\",\"content\":\"\"}}]}");
                    Assert.True(isr.IsErr);
                    Assert.Equal(RimMindErrorCode.ClientTransientFailure, isr.Error.Code);

                    var aborted = DeepSeekProtocolAdapter.ParseResponse("{\"choices\":[{\"finish_reason\":\"aborted\",\"message\":{\"role\":\"assistant\",\"content\":\"\"}}]}");
                    Assert.True(aborted.IsErr);
                    Assert.Equal(RimMindErrorCode.ClientTransientFailure, aborted.Error.Code);

                    var ws = DeepSeekProtocolAdapter.ParseResponse("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"   \"}}]}");
                    Assert.True(ws.IsErr);
                    Assert.Equal(RimMindErrorCode.ClientTransientFailure, ws.Error.Code);
                }));
        }

        [Fact]
        public async Task ModelServiceClient_handles_quota_isolation_and_content_filter_on_deepseek()
        {
            await ContractCaseRunner.RunAsync(
                ("HTTP 402 Insufficient Balance isolates primary immediately and fails over to backup", async () =>
                {
                    long clock = 10_000;
                    var primary = new ModelEndpointConfig
                    {
                        id = "ds-primary",
                        name = "DeepSeek Primary",
                        protocolId = "deepseek",
                        providerType = ProviderType.DeepSeekOfficial,
                        endpoint = "https://api.deepseek.com",
                        apiKey = "sk-ds-primary-secret-1234",
                        modelName = "deepseek-flash",
                        priority = 0,
                        isEnabled = true
                    };
                    var backup = new ModelEndpointConfig
                    {
                        id = "ds-backup",
                        name = "DeepSeek Backup",
                        protocolId = "deepseek",
                        providerType = ProviderType.DeepSeekOfficial,
                        endpoint = "https://api.deepseek.com",
                        apiKey = "sk-ds-backup-secret-5678",
                        modelName = "deepseek-v4-pro",
                        priority = 1,
                        isEnabled = true
                    };

                    int calls = 0;
                    using var client = new ModelServiceClient(
                        () => new List<ModelEndpointConfig> { primary, backup },
                        () => BalancingStrategy.PriorityFailover,
                        loadBalancer: null,
                        httpSendOverride: (req, _) =>
                        {
                            calls++;
                            if (calls == 1)
                            {
                                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)402)
                                {
                                    Content = new StringContent("{\"error\":{\"message\":\"Insufficient Balance\"}}", Encoding.UTF8, "application/json")
                                });
                            }
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new StringContent("{\"id\":\"ok-2\",\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"Backup ok\"}}],\"usage\":{\"total_tokens\":10}}", Encoding.UTF8, "application/json")
                            });
                        },
                        clockMsOverride: () => clock);

                    var res = await client.SendAsync(new LlmRequestEnvelope
                    {
                        RequestId = "req-402-failover",
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "hi" } }
                    });

                    Assert.True(res.IsOk);
                    Assert.Equal("Backup ok", res.Value.Content);
                    Assert.Equal(2, calls);
                    Assert.True(primary.IsTemporarilyIsolated(clock));
                    Assert.False(primary.isHealthy);
                }),
                ("HTTP 402 across all candidates preserves ClientQuotaOrAuthFailure", async () =>
                {
                    long clock = 20_000;
                    var node = new ModelEndpointConfig
                    {
                        id = "ds-only",
                        name = "DeepSeek Only",
                        protocolId = "deepseek",
                        endpoint = "https://api.deepseek.com",
                        apiKey = "sk-ds-secret-only-1234",
                        modelName = "deepseek-flash",
                        isEnabled = true
                    };

                    using var client = new ModelServiceClient(
                        () => new List<ModelEndpointConfig> { node },
                        () => BalancingStrategy.PriorityFailover,
                        loadBalancer: null,
                        httpSendOverride: (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)402)
                        {
                            Content = new StringContent("{\"error\":{\"message\":\"Insufficient Balance for sk-ds-secret-only-1234\"}}", Encoding.UTF8, "application/json")
                        }),
                        clockMsOverride: () => clock);

                    var res = await client.SendAsync(new LlmRequestEnvelope
                    {
                        RequestId = "req-402-all",
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "hi" } }
                    });

                    Assert.True(res.IsErr);
                    Assert.Equal(RimMindErrorCode.ClientQuotaOrAuthFailure, res.Error.Code);
                    Assert.DoesNotContain("sk-ds-secret-only-1234", res.Error.Message);
                    Assert.True(node.IsTemporarilyIsolated(clock));
                }),
                ("HTTP 400 Content Exists Risk stops immediately without penalizing primary or trying backup", async () =>
                {
                    long clock = 30_000;
                    var primary = new ModelEndpointConfig
                    {
                        id = "ds-p",
                        name = "Primary",
                        protocolId = "deepseek",
                        endpoint = "https://api.deepseek.com",
                        apiKey = "sk-ds-p",
                        isEnabled = true
                    };
                    var backup = new ModelEndpointConfig
                    {
                        id = "ds-b",
                        name = "Backup",
                        protocolId = "deepseek",
                        endpoint = "https://api.deepseek.com",
                        apiKey = "sk-ds-b",
                        priority = 1,
                        isEnabled = true
                    };

                    int calls = 0;
                    using var client = new ModelServiceClient(
                        () => new List<ModelEndpointConfig> { primary, backup },
                        () => BalancingStrategy.PriorityFailover,
                        loadBalancer: null,
                        httpSendOverride: (_, _) =>
                        {
                            calls++;
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                            {
                                Content = new StringContent("{\"error\":{\"message\":\"Content Exists Risk\"}}", Encoding.UTF8, "application/json")
                            });
                        },
                        clockMsOverride: () => clock);

                    var res = await client.SendAsync(new LlmRequestEnvelope
                    {
                        RequestId = "req-ds-400",
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "hi" } }
                    });

                    Assert.True(res.IsErr);
                    Assert.Equal(RimMindErrorCode.ContentFiltered, res.Error.Code);
                    Assert.Equal(1, calls);
                    Assert.Equal(0, primary.consecutiveFailures);
                    Assert.False(primary.IsTemporarilyIsolated(clock));
                }));
        }

        [Fact]
        public void DeepSeekModelProtocol_factory_probe_and_scribe_roundtrip_work()
        {
            ContractCaseRunner.Run(
                ("Registry resolves deepseek protocol, capabilities, POST URI normalization, and GET health probe", () =>
                {
                    var protocol = ModelProtocolRegistry.Resolve(ModelProtocolRegistry.ToProtocolId(ProviderType.DeepSeekOfficial));
                    Assert.Same(DeepSeekModelProtocol.Instance, protocol);
                    Assert.Equal("deepseek", protocol.ProtocolId);
                    var contextual = Assert.IsAssignableFrom<IContextualModelProtocol>(protocol);
                    Assert.True(contextual.Capabilities.SupportsNativeThinking);
                    Assert.True(contextual.Capabilities.SupportsSafetyCheck);
                    Assert.False(contextual.Capabilities.SupportsJsonSchema);
                    Assert.True(contextual.Capabilities.RequiresReasoningContentInToolHistory);

                    var epWithSuffix = new ModelEndpointConfig
                    {
                        endpoint = "https://api.deepseek.com/chat/completions",
                        apiKey = "ds-k",
                        protocolId = "deepseek"
                    };

                    using var postReq = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions/chat/completions");
                    protocol.ApplyHeaders(postReq, epWithSuffix, "req-1");
                    Assert.Equal("https://api.deepseek.com/chat/completions", postReq.RequestUri?.ToString());
                    Assert.False(postReq.Headers.Contains("x-opencode-session"));

                    using var probeReq = protocol.BuildHealthProbeRequest(epWithSuffix, "ignored");
                    Assert.Equal(HttpMethod.Get, probeReq.Method);
                    Assert.Equal("https://api.deepseek.com/models", probeReq.RequestUri?.ToString());
                }),
                ("ModelEndpointConfig thinkingPolicy and safetyLevel round-trip via Scribe_Values", () =>
                {
                    var buffer = new Dictionary<string, object?>();
                    var original = new ModelEndpointConfig
                    {
                        id = "ds-save-1",
                        name = "DeepSeek Saved",
                        protocolId = "deepseek",
                        providerType = ProviderType.DeepSeekOfficial,
                        endpoint = "https://api.deepseek.com",
                        modelName = "deepseek-v4-pro",
                        thinkingPolicy = EndpointThinkingPolicy.ForceDisabled,
                        safetyLevel = EndpointSafetyLevel.High,
                        isEnabled = true
                    };

                    try
                    {
                        Verse.Scribe_Values.BeginSave(buffer);
                        original.ExposeData();
                        Verse.Scribe_Values.End();

                        var loaded = new ModelEndpointConfig();
                        Verse.Scribe_Values.BeginLoad(buffer, Verse.LoadSaveMode.LoadingVars);
                        loaded.ExposeData();
                        Verse.Scribe.mode = Verse.LoadSaveMode.PostLoadInit;
                        loaded.ExposeData();

                        Assert.Equal(EndpointThinkingPolicy.ForceDisabled, loaded.thinkingPolicy);
                        Assert.Equal(EndpointSafetyLevel.High, loaded.safetyLevel);
                        Assert.Equal(ProviderType.DeepSeekOfficial, loaded.providerType);
                        Assert.Equal("deepseek", loaded.EffectiveProtocolId);
                    }
                    finally
                    {
                        Verse.Scribe_Values.End();
                    }
                }),
                ("DeepSeekClientFactory exposes metadata and falls back to Core settings when pool empty", () =>
                {
                    var prevSettings = RimMindModelServiceMod.Settings;
                    try
                    {
                        RimMindModelServiceMod.Settings = new ModelServiceSettings { enableService = false };
                        var factory = new DeepSeekClientFactory();
                        Assert.Equal("deepseek_native", factory.ProviderId);
                        Assert.Equal(35, factory.OrderWeight);
                        Assert.Equal("deepseek-flash", factory.DefaultModelName);
                        Assert.True(factory.VisibleInMenu);

                        var configuredClient = factory.Create(new LocalSettingsStub
                        {
                            ApiKey = "ds-key-ok",
                            ApiEndpoint = "https://api.openai.com/v1",
                            ModelName = "deepseek-v4-pro"
                        });
                        Assert.True(configuredClient.IsConfigured());

                        var unconfiguredClient = factory.Create(new LocalSettingsStub { ApiKey = "" });
                        Assert.False(unconfiguredClient.IsConfigured());
                    }
                    finally
                    {
                        RimMindModelServiceMod.Settings = prevSettings;
                    }
                }));
        }

        [Fact]
        public async Task DeepSeekSpeechSynthesizer_and_Recognizer_fulfill_audio_ports()
        {
            await ContractCaseRunner.RunAsync(
                ("TTS uses voice_id/format/filter_markdown wire fields, counts Hanzi as 2 billing chars (<=10000), and maps moderation", async () =>
                {
                    Uri? capturedUri = null;
                    string? capturedJson = null;

                    var tts = new DeepSeekSpeechSynthesizer(
                        async (req, _) =>
                        {
                            capturedUri = req.RequestUri;
                            capturedJson = req.Content != null ? await req.Content.ReadAsStringAsync() : null;
                            return new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new ByteArrayContent(new byte[] { 0x52, 0x49, 0x46, 0x46 })
                            };
                        },
                        () => ("https://api.deepseek.com/v1/chat/completions", "sk-tts-secret-9999"));

                    Assert.True(tts.IsConfigured());
                    Assert.Equal(4, DeepSeekSpeechSynthesizer.ComputeBillingCharacters("a中b"));

                    var okRes = await tts.SynthesizeAsync(new SpeechSynthesisRequest
                    {
                        Text = "**Alert**: colony ready!",
                        VoiceId = "male-deep",
                        PreferredFormat = AudioContainerFormat.Wav,
                        StripMarkdown = true
                    });

                    Assert.True(okRes.IsOk);
                    Assert.Equal("https://api.deepseek.com/v1/tools/tts", capturedUri?.ToString());
                    var bodyObj = JObject.Parse(capturedJson!);
                    Assert.Equal("**Alert**: colony ready!", bodyObj["text"]?.ToString());
                    Assert.Equal("tide", bodyObj["voice_id"]?.ToString());
                    Assert.Equal("wav", bodyObj["format"]?.ToString());
                    Assert.True(bodyObj["filter_markdown"]?.Value<bool>() ?? false);
                    Assert.Null(bodyObj["output_format"]);
                    Assert.Equal(AudioContainerFormat.Wav, okRes.Value.Format);
                    Assert.Equal(new byte[] { 0x52, 0x49, 0x46, 0x46 }, okRes.Value.AudioBytes);

                    // 5001 Hanzi = 10002 billing chars -> blocked pre-flight without HTTP call
                    int extraCalls = 0;
                    var guardedTts = new DeepSeekSpeechSynthesizer(
                        (_, _) =>
                        {
                            extraCalls++;
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                        },
                        () => ("https://api.deepseek.com", "sk-tts"));
                    var tooLong = await guardedTts.SynthesizeAsync(new SpeechSynthesisRequest { Text = new string('中', 5001) });
                    Assert.True(tooLong.IsErr);
                    Assert.Equal(RimMindErrorCode.ClientPermanentFailure, tooLong.Error.Code);
                    Assert.Equal(0, extraCalls);

                    // HTTP 400 Content Exists Risk redacts secret and classifies ContentFiltered
                    var modTts = new DeepSeekSpeechSynthesizer(
                        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent("{\"error\":{\"message\":\"Content Exists Risk key sk-tts-secret-9999\"}}", Encoding.UTF8, "application/json")
                        }),
                        () => ("https://api.deepseek.com", "sk-tts-secret-9999"));
                    var modRes = await modTts.SynthesizeAsync(new SpeechSynthesisRequest { Text = "blocked text" });
                    Assert.True(modRes.IsErr);
                    Assert.Equal(RimMindErrorCode.ContentFiltered, modRes.Error.Code);
                    Assert.DoesNotContain("sk-tts-secret-9999", modRes.Error.Message);
                }),
                ("ASR posts multipart file (without hotwords), enforces 8 MiB guard, and parses response", async () =>
                {
                    Uri? capturedUri = null;
                    string? capturedMultipartText = null;

                    var asr = new DeepSeekSpeechRecognizer(
                        async (req, _) =>
                        {
                            capturedUri = req.RequestUri;
                            capturedMultipartText = req.Content != null ? await req.Content.ReadAsStringAsync() : null;
                            return new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new StringContent(
                                    "{\"request_id\":\"asr-77\",\"text\":\"Draft colonists\",\"duration_ms\":1420}",
                                    Encoding.UTF8,
                                    "application/json")
                            };
                        },
                        () => ("https://api.deepseek.com/v1", "sk-asr-secret"));

                    Assert.True(asr.IsConfigured());
                    var res = await asr.TranscribeAsync(new SpeechRecognitionRequest
                    {
                        AudioBytes = new byte[] { 1, 2, 3 },
                        Format = AudioContainerFormat.Wav,
                        FileName = "command.wav",
                        VocabularyHints = "Cassandra,Draft"
                    });

                    Assert.True(res.IsOk);
                    Assert.Equal("https://api.deepseek.com/v1/tools/asr", capturedUri?.ToString());
                    Assert.DoesNotContain("hotwords", capturedMultipartText);
                    Assert.Contains("command.wav", capturedMultipartText);
                    Assert.Equal("asr-77", res.Value.RequestId);
                    Assert.Equal("Draft colonists", res.Value.Text);
                    Assert.Equal(1420, res.Value.DurationMs);

                    var oversized = await asr.TranscribeAsync(new SpeechRecognitionRequest
                    {
                        AudioBytes = new byte[(8 * 1024 * 1024) + 1],
                        Format = AudioContainerFormat.Wav
                    });
                    Assert.True(oversized.IsErr);
                    Assert.Equal(RimMindErrorCode.ClientPermanentFailure, oversized.Error.Code);
                }),
                ("EnvironmentEndpointOverlay applies RIMMIND_DEEPSEEK_API_KEY to keyless deepseek node without mutating persisted settings", () =>
                {
                    var env = new Dictionary<string, string>
                    {
                        ["RIMMIND_DEEPSEEK_API_KEY"] = "sk-env-ds-only",
                        ["RIMMIND_DEEPSEEK_ENDPOINT"] = "https://api.deepseek.com",
                        ["RIMMIND_DEEPSEEK_MODEL"] = "deepseek-flash"
                    };
                    var overlay = EnvironmentEndpointOverlay.FromEnvironment(k => env.TryGetValue(k, out var v) ? v : null);
                    Assert.NotNull(overlay);

                    var dsKeyless = new ModelEndpointConfig
                    {
                        id = "ds-1",
                        protocolId = "deepseek",
                        providerType = ProviderType.DeepSeekOfficial,
                        apiKey = "",
                        endpoint = "https://api.deepseek.com",
                        modelName = "deepseek-flash",
                        thinkingPolicy = EndpointThinkingPolicy.FollowRequest,
                        safetyLevel = EndpointSafetyLevel.High
                    };
                    var openAiOther = new ModelEndpointConfig
                    {
                        id = "oa-1",
                        protocolId = "openai",
                        providerType = ProviderType.OpenAICompatible,
                        apiKey = ""
                    };

                    var applied = overlay!.Apply(new List<ModelEndpointConfig> { dsKeyless, openAiOther });
                    Assert.Equal("", dsKeyless.apiKey);
                    Assert.NotSame(dsKeyless, applied[0]);
                    Assert.Equal("sk-env-ds-only", applied[0].apiKey);
                    Assert.Equal(EndpointSafetyLevel.High, applied[0].safetyLevel);
                    Assert.Same(openAiOther, applied[1]);
                    Assert.Equal("", applied[1].apiKey);

                    return Task.CompletedTask;
                }));
        }

        [Fact]
        public async Task Live_DeepSeek_chat_tool_loop_and_tts_asr_roundtrip_when_enabled()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("RIMMIND_LIVE_DEEPSEEK_TEST"), "1", StringComparison.Ordinal))
            {
                return;
            }

            string? liveKey = Environment.GetEnvironmentVariable("RIMMIND_DEEPSEEK_API_KEY");
            if (string.IsNullOrWhiteSpace(liveKey))
                liveKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            Assert.False(string.IsNullOrWhiteSpace(liveKey), "RIMMIND_DEEPSEEK_API_KEY must be set when RIMMIND_LIVE_DEEPSEEK_TEST=1.");

            var node = new ModelEndpointConfig
            {
                id = "live-ds",
                name = "Live DeepSeek Official",
                protocolId = "deepseek",
                providerType = ProviderType.DeepSeekOfficial,
                endpoint = "https://api.deepseek.com",
                apiKey = liveKey!.Trim(),
                modelName = "deepseek-flash",
                thinkingPolicy = EndpointThinkingPolicy.FollowRequest,
                safetyLevel = EndpointSafetyLevel.High,
                isEnabled = true,
                timeoutSeconds = 45
            };

            using var client = new ModelServiceClient(
                () => new List<ModelEndpointConfig> { node },
                () => BalancingStrategy.PriorityFailover);

            // 1. Non-thinking + safety_check=high
            var fastRes = await client.SendAsync(new LlmRequestEnvelope
            {
                RequestId = "live-fast-1",
                NpcId = "Pawn_LiveProbe",
                ReasoningEffort = ReasoningEffort.None,
                MaxTokens = 32,
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "user", Content = "Reply with OK only." }
                }
            });
            Assert.True(fastRes.IsOk, fastRes.IsErr ? fastRes.Error.Message : "");
            Assert.False(string.IsNullOrWhiteSpace(fastRes.Value.Content));

            // 2. Thinking (low) + multi-turn ToolCall round-trip verifying reasoning_content preservation
            var weatherTool = new StructuredTool
            {
                Name = "check_colony_temp",
                Description = "Get outdoor colony temperature in Celsius.",
                Parameters = "{\"type\":\"object\",\"properties\":{\"sector\":{\"type\":\"string\"}},\"required\":[\"sector\"]}",
                ToolChoice = "required"
            };
            var userPrompt = new ChatMessage { Role = "user", Content = "Call check_colony_temp for sector A." };

            var turn1 = await client.SendAsync(new LlmRequestEnvelope
            {
                RequestId = "live-tool-1",
                NpcId = "Pawn_LiveProbe",
                ReasoningEffort = ReasoningEffort.Low,
                MaxTokens = 256,
                Tools = new List<StructuredTool> { weatherTool },
                Messages = new List<ChatMessage> { userPrompt }
            });
            Assert.True(turn1.IsOk, turn1.IsErr ? turn1.Error.Message : "");
            Assert.False(string.IsNullOrWhiteSpace(turn1.Value.ToolCallsJson));
            Assert.False(string.IsNullOrWhiteSpace(turn1.Value.ReasoningContent));

            var toolArray = JArray.Parse(turn1.Value.ToolCallsJson!);
            Assert.True(toolArray.Count > 0);
            string tcId = toolArray[0]["id"]!.ToString();
            string tcName = toolArray[0]["function"]!["name"]!.ToString();
            string tcArgs = toolArray[0]["function"]!["arguments"]!.ToString();

            var turn2 = await client.SendAsync(new LlmRequestEnvelope
            {
                RequestId = "live-tool-2",
                NpcId = "Pawn_LiveProbe",
                ReasoningEffort = ReasoningEffort.Low,
                MaxTokens = 128,
                Tools = new List<StructuredTool>
                {
                    new StructuredTool
                    {
                        Name = "check_colony_temp",
                        Description = weatherTool.Description,
                        Parameters = weatherTool.Parameters,
                        ToolChoice = "auto"
                    }
                },
                Messages = new List<ChatMessage>
                {
                    userPrompt,
                    new ChatMessage
                    {
                        Role = "assistant",
                        Content = turn1.Value.Content,
                        ReasoningContent = turn1.Value.ReasoningContent,
                        ToolCalls = new List<ChatToolCall>
                        {
                            new ChatToolCall { Id = tcId, Name = tcName, Arguments = tcArgs }
                        }
                    },
                    new ChatMessage
                    {
                        Role = "tool",
                        ToolCallId = tcId,
                        Content = "{\"temp_c\": 21.5}"
                    }
                }
            });
            Assert.True(turn2.IsOk, turn2.IsErr ? turn2.Error.Message : "");
            Assert.False(string.IsNullOrWhiteSpace(turn2.Value.Content));

            // 3. Live TTS -> ASR round-trip
            var tts = new DeepSeekSpeechSynthesizer(null, () => ("https://api.deepseek.com", liveKey.Trim()));
            var synthRes = await tts.SynthesizeAsync(new SpeechSynthesisRequest
            {
                Text = "RimMind DeepSeek voice check online.",
                VoiceId = "stella",
                PreferredFormat = AudioContainerFormat.Wav,
                StripMarkdown = true
            });
            Assert.True(synthRes.IsOk, synthRes.IsErr ? synthRes.Error.Message : "");
            Assert.True(synthRes.Value.AudioBytes.Length > 44);
            Assert.Equal((byte)'R', synthRes.Value.AudioBytes[0]);
            Assert.Equal((byte)'I', synthRes.Value.AudioBytes[1]);
            Assert.Equal((byte)'F', synthRes.Value.AudioBytes[2]);
            Assert.Equal((byte)'F', synthRes.Value.AudioBytes[3]);

            var asr = new DeepSeekSpeechRecognizer(null, () => ("https://api.deepseek.com", liveKey.Trim()));
            var recRes = await asr.TranscribeAsync(new SpeechRecognitionRequest
            {
                AudioBytes = synthRes.Value.AudioBytes,
                Format = AudioContainerFormat.Wav,
                FileName = "live_probe.wav"
            });
            Assert.True(recRes.IsOk, recRes.IsErr ? recRes.Error.Message : "");
            Assert.False(string.IsNullOrWhiteSpace(recRes.Value.Text));
            Assert.True(recRes.Value.DurationMs > 0);
        }
    }
}
