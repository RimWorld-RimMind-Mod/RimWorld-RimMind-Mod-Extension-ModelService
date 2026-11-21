using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.ModelService.Balancing;
using RimMind.ModelService.Client;
using RimMind.ModelService.Diagnostics;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;
using RimMind.Testing;
using Xunit;

namespace RimMind.ModelService.Tests.Contracts
{
    public sealed class ProtocolCapabilitiesAndBoundaryContracts
    {
        [Fact]
        public async Task ProtocolCapabilities_drive_loopback_security_and_completion_uri_routing()
        {
            await ContractCaseRunner.RunAsync(
                ("built-in protocols expose expected capabilities and resolve completion URIs without client hardcoding", () =>
                {
                    Assert.False(OpenAIModelProtocol.Instance.Capabilities.RequiresLoopback);
                    Assert.True(LocalGatewayModelProtocol.Instance.Capabilities.RequiresLoopback);
                    Assert.True(OpenCodeGoModelProtocol.Instance.Capabilities.HasStatusBadge);
                    Assert.True(DeepSeekModelProtocol.Instance.Capabilities.SupportsNativeThinking);
                    Assert.True(DeepSeekModelProtocol.Instance.Capabilities.SupportsSafetyCheck);

                    Assert.Equal(
                        "https://api.openai.com/v1/chat/completions",
                        OpenAIModelProtocol.Instance.ResolveCompletionUri(new ModelEndpointConfig { endpoint = "https://api.openai.com/v1/" }).ToString());
                    Assert.Equal(
                        "https://api.anthropic.com/v1/messages",
                        AnthropicModelProtocol.Instance.ResolveCompletionUri(new ModelEndpointConfig { endpoint = "https://api.anthropic.com/v1/" }).ToString());
                    Assert.Equal(
                        "https://api.deepseek.com/chat/completions",
                        DeepSeekModelProtocol.Instance.ResolveCompletionUri(new ModelEndpointConfig { endpoint = "https://api.deepseek.com/chat/completions" }).ToString());
                    return Task.CompletedTask;
                }),
                ("custom protocol with RequiresLoopback=true and OpenAICompatible legacy type is enforced by both client and health probe", async () =>
                {
                    var customLoopback = new CustomLoopbackOnlyProtocol();
                    ModelProtocolRegistry.Register(customLoopback);
                    try
                    {
                        var remoteNode = new ModelEndpointConfig
                        {
                            id = "custom-loopback-remote",
                            name = "Custom Remote",
                            endpoint = "https://public.example.com/v1",
                            protocolId = customLoopback.ProtocolId,
                            isEnabled = true
                        };
                        Assert.Equal(ProviderType.OpenAICompatible, remoteNode.providerType);

                        int sendCount = 0;
                        using var client = new ModelServiceClient(
                            () => new List<ModelEndpointConfig> { remoteNode },
                            () => BalancingStrategy.PriorityFailover,
                            loadBalancer: null,
                            httpSendOverride: (_, _) =>
                            {
                                sendCount++;
                                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                            });

                        var clientRes = await client.SendAsync(new LlmRequestEnvelope { RequestId = "req-custom-loopback" });
                        Assert.True(clientRes.IsErr);
                        Assert.Equal(RimMindErrorCode.ClientPermanentFailure, clientRes.Error.Code);
                        Assert.Contains("Security violation", clientRes.Error.Message);
                        Assert.Equal(0, sendCount);

                        var (probeOk, _, probeMsg) = await EndpointHealthProbe.ProbeEndpointAsync(
                            remoteNode,
                            default,
                            (_, _) =>
                            {
                                sendCount++;
                                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                            });
                        Assert.False(probeOk);
                        Assert.Contains("Security violation", probeMsg);
                        Assert.Equal(0, sendCount);

                        var localNode = new ModelEndpointConfig
                        {
                            id = "custom-loopback-local",
                            name = "Custom Local",
                            endpoint = "http://127.0.0.1:8787/v1",
                            protocolId = customLoopback.ProtocolId,
                            isEnabled = true
                        };

                        Uri? capturedUri = null;
                        using var localClient = new ModelServiceClient(
                            () => new List<ModelEndpointConfig> { localNode },
                            () => BalancingStrategy.PriorityFailover,
                            loadBalancer: null,
                            httpSendOverride: (req, _) =>
                            {
                                capturedUri = req.RequestUri;
                                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                                {
                                    Content = new StringContent(
                                        "{\"id\":\"ok-local\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"local ok\"}}]}",
                                        Encoding.UTF8,
                                        "application/json")
                                });
                            });

                        var okRes = await localClient.SendAsync(new LlmRequestEnvelope { RequestId = "req-custom-local" });
                        Assert.True(okRes.IsOk);
                        Assert.Equal("http://127.0.0.1:8787/v1/custom/complete", capturedUri?.ToString());
                    }
                    finally
                    {
                        ModelProtocolRegistry.Unregister(customLoopback.ProtocolId);
                    }
                }));
        }

        [Fact]
        public void OpenAIModelProtocol_does_not_sniff_opencode_headers_while_OpenCodeGo_mounts_session_header()
        {
            ContractCaseRunner.Run(
                ("OpenAIModelProtocol never sniffs opencode URL or oc_sk_ key", () =>
                {
                    var ep = new ModelEndpointConfig
                    {
                        endpoint = "https://opencode.ai/zen/go/v1",
                        apiKey = "oc_sk_test_key_12345678",
                        protocolId = ModelProtocolRegistry.OpenAIProtocolId
                    };
                    using var req = new HttpRequestMessage(HttpMethod.Post, OpenAIModelProtocol.Instance.ResolveCompletionUri(ep));
                    OpenAIModelProtocol.Instance.ApplyHeaders(req, ep, "req-123");

                    Assert.False(req.Headers.Contains("x-opencode-session"));
                    Assert.True(req.Headers.TryGetValues("Authorization", out var authVals));
                    Assert.Equal("Bearer oc_sk_test_key_12345678", authVals!.Single());
                }),
                ("OpenCodeGoModelProtocol mounts x-opencode-session regardless of URL or key prefix", () =>
                {
                    var ep = new ModelEndpointConfig
                    {
                        endpoint = "https://custom-proxy.example/v1",
                        apiKey = "plain_key_value",
                        protocolId = ModelProtocolRegistry.OpenCodeGoProtocolId
                    };
                    using var req = new HttpRequestMessage(HttpMethod.Post, OpenCodeGoModelProtocol.Instance.ResolveCompletionUri(ep));
                    OpenCodeGoModelProtocol.Instance.ApplyHeaders(req, ep, "pawn-7");

                    Assert.True(req.Headers.TryGetValues("x-opencode-session", out var sessionVals));
                    Assert.Equal("rimmind-pawn-7", sessionVals!.Single());
                }));
        }

        [Fact]
        public async Task ModelServiceClient_SendStreamAsync_delivers_chunks_without_silent_drop()
        {
            await ContractCaseRunner.RunAsync(
                ("successful SendStreamAsync emits reasoning, content, tool calls, usage, and terminal FinalResponse chunks", async () =>
                {
                    var node = new ModelEndpointConfig
                    {
                        id = "stream-ok",
                        name = "Stream Node",
                        endpoint = "https://api.openai.com/v1",
                        apiKey = "sk-test",
                        isEnabled = true
                    };
                    using var client = new ModelServiceClient(
                        () => new List<ModelEndpointConfig> { node },
                        () => BalancingStrategy.PriorityFailover,
                        loadBalancer: null,
                        httpSendOverride: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                "{\"id\":\"cmpl-1\",\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"Done\",\"reasoning_content\":\"Thought\"}}],\"usage\":{\"prompt_tokens\":20,\"completion_tokens\":5,\"total_tokens\":25,\"prompt_tokens_details\":{\"cached_tokens\":12}}}",
                                Encoding.UTF8,
                                "application/json")
                        }));

                    var chunks = new List<LlmChunk>();
                    var res = await client.SendStreamAsync(
                        new LlmRequestEnvelope { RequestId = "req-stream-1", Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "hi" } } },
                        chunk => chunks.Add(chunk));

                    Assert.True(res.IsOk);
                    Assert.Contains(chunks, c => c.DeltaReasoningContent == "Thought");
                    Assert.Contains(chunks, c => c.DeltaContent == "Done");
                    Assert.Contains(chunks, c => c.DeltaPromptTokens == 20 && c.DeltaCompletionTokens == 5 && c.DeltaCachedTokens == 12);
                    var last = chunks.Last();
                    Assert.True(last.IsLast);
                    Assert.NotNull(last.FinalResponse);
                    Assert.Equal("req-stream-1", last.FinalResponse!.RequestId);
                    Assert.Equal("Done", last.FinalResponse.Content);
                }),
                ("failed SendStreamAsync emits zero chunks and null callback does not throw", async () =>
                {
                    var node = new ModelEndpointConfig
                    {
                        id = "stream-err",
                        name = "Err Node",
                        endpoint = "https://api.openai.com/v1",
                        isEnabled = true
                    };
                    using var errClient = new ModelServiceClient(
                        () => new List<ModelEndpointConfig> { node },
                        () => BalancingStrategy.PriorityFailover,
                        loadBalancer: null,
                        httpSendOverride: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent("{\"error\":\"bad request\"}", Encoding.UTF8, "application/json")
                        }));

                    var chunks = new List<LlmChunk>();
                    var errRes = await errClient.SendStreamAsync(
                        new LlmRequestEnvelope { RequestId = "req-stream-err" },
                        chunk => chunks.Add(chunk));
                    Assert.True(errRes.IsErr);
                    Assert.Empty(chunks);

                    using var okClient = new ModelServiceClient(
                        () => new List<ModelEndpointConfig> { node },
                        () => BalancingStrategy.PriorityFailover,
                        loadBalancer: null,
                        httpSendOverride: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"id\":\"ok\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"ok\"}}]}", Encoding.UTF8, "application/json")
                        }));
                    var nullCbRes = await okClient.SendStreamAsync(new LlmRequestEnvelope { RequestId = "req-null-cb" }, null!);
                    Assert.True(nullCbRes.IsOk);
                }));
        }

        [Fact]
        public async Task LoadBalancer_and_ModelServiceClient_integrate_Core_EndpointFailoverPolicy()
        {
            await ContractCaseRunner.RunAsync(
                ("HTTP 429 rate-limit triggers Core EndpointFailoverPolicy short cooldown and auto-recovers after window", async () =>
                {
                    long nowMs = 100_000L;
                    var primary = new ModelEndpointConfig { id = "rl-primary", name = "Primary", endpoint = "https://p.example/v1", priority = 0, isEnabled = true };
                    var backup = new ModelEndpointConfig { id = "rl-backup", name = "Backup", endpoint = "https://b.example/v1", priority = 1, isEnabled = true };

                    var hitOrder = new List<string>();
                    bool primaryShouldRateLimit = true;

                    using var client = new ModelServiceClient(
                        () => new List<ModelEndpointConfig> { primary, backup },
                        () => BalancingStrategy.PriorityFailover,
                        loadBalancer: null,
                        httpSendOverride: (req, _) =>
                        {
                            bool isPrimary = req.RequestUri!.Host == "p.example";
                            hitOrder.Add(isPrimary ? "primary" : "backup");
                            if (isPrimary && primaryShouldRateLimit)
                            {
                                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429)
                                {
                                    Content = new StringContent("{\"error\":{\"message\":\"rate_limit exceeded\"}}", Encoding.UTF8, "application/json")
                                });
                            }
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new StringContent("{\"id\":\"ok\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"ok\"}}]}", Encoding.UTF8, "application/json")
                            });
                        },
                        clockMsOverride: () => nowMs);

                    // Request 1: hits primary (429), fails over to backup
                    var r1 = await client.SendAsync(new LlmRequestEnvelope { RequestId = "rl-1" });
                    Assert.True(r1.IsOk);
                    Assert.Equal(new[] { "primary", "backup" }, hitOrder);

                    // Request 2 at +500ms (inside 2s transient rate-limit cooldown): skips primary, hits backup directly
                    nowMs += 500L;
                    hitOrder.Clear();
                    var r2 = await client.SendAsync(new LlmRequestEnvelope { RequestId = "rl-2" });
                    Assert.True(r2.IsOk);
                    Assert.Equal(new[] { "backup" }, hitOrder);

                    // Request 3 at +2500ms (past 2s cooldown): primary is eligible again and succeeds
                    nowMs += 2_500L;
                    primaryShouldRateLimit = false;
                    hitOrder.Clear();
                    var r3 = await client.SendAsync(new LlmRequestEnvelope { RequestId = "rl-3" });
                    Assert.True(r3.IsOk);
                    Assert.Equal(new[] { "primary" }, hitOrder);
                }),
                ("health probe RecordSuccess clears EndpointFailoverPolicy exhaustion on the recovered node", async () =>
                {
                    long nowMs = 200_000L;
                    var primary = new ModelEndpointConfig { id = "rec-primary", name = "Primary", endpoint = "https://p.example/v1", priority = 0, isEnabled = true };
                    var backup = new ModelEndpointConfig { id = "rec-backup", name = "Backup", endpoint = "https://b.example/v1", priority = 1, isEnabled = true };

                    var policy = new EndpointFailoverPolicy(clockMs: () => nowMs);
                    var lb = new LoadBalancer(policy);

                    int primaryCalls = 0;
                    bool primaryExhausted = true;
                    using var client = new ModelServiceClient(
                        () => new List<ModelEndpointConfig> { primary, backup },
                        () => BalancingStrategy.PriorityFailover,
                        loadBalancer: lb,
                        httpSendOverride: (req, _) =>
                        {
                            if (req.RequestUri!.Host == "p.example")
                            {
                                primaryCalls++;
                                if (primaryExhausted)
                                {
                                    return Task.FromResult(new HttpResponseMessage((HttpStatusCode)402)
                                    {
                                        Content = new StringContent("{\"error\":{\"message\":\"Insufficient Balance\"}}", Encoding.UTF8, "application/json")
                                    });
                                }
                            }
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new StringContent("{\"id\":\"ok\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"ok\"}}]}", Encoding.UTF8, "application/json")
                            });
                        },
                        clockMsOverride: () => nowMs);

                    var r1 = await client.SendAsync(new LlmRequestEnvelope { RequestId = "rec-1" });
                    Assert.True(r1.IsOk);
                    Assert.Equal(1, primaryCalls);
                    Assert.True(policy.IsPermanentlyExhausted(LoadBalancer.BuildNodeKey(primary), nowMs));

                    // Still exhausted 10s later -> primary skipped
                    nowMs += 10_000L;
                    var r2 = await client.SendAsync(new LlmRequestEnvelope { RequestId = "rec-2" });
                    Assert.True(r2.IsOk);
                    Assert.Equal(1, primaryCalls);

                    // Health probe succeeds -> RecordSuccess increments SuccessGeneration -> LoadBalancer clears isolation
                    primaryExhausted = false;
                    var (probeOk, _, _) = await EndpointHealthProbe.ProbeEndpointAsync(
                        primary,
                        default,
                        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
                    Assert.True(probeOk);

                    var r3 = await client.SendAsync(new LlmRequestEnvelope { RequestId = "rec-3" });
                    Assert.True(r3.IsOk);
                    Assert.Equal(2, primaryCalls);
                }));
        }

        [Fact]
        public void ModelService_source_maintains_Core_layer_boundary_and_hygiene_invariants()
        {
            string sourceDir = FindModelServiceSourceDir();
            Assert.True(Directory.Exists(sourceDir), $"ModelService Source dir must exist: {sourceDir}");

            var csFiles = Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(seg =>
                    seg.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    seg.Equals("obj", StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            ContractCaseRunner.Run(
                ("zero RimMind.Infrastructure imports or references in ModelService Source", () =>
                {
                    var violations = csFiles
                        .Where(f => StripComments(File.ReadAllText(f)).IndexOf("RimMind.Infrastructure", StringComparison.Ordinal) >= 0)
                        .Select(f => Path.GetRelativePath(sourceDir, f))
                        .ToArray();
                    Assert.True(violations.Length == 0, $"Boundary leak(s): {string.Join(", ", violations)}");
                }),
                ("zero ProviderType.LocalSubscriptionGateway or ProtocolId switches in Client, Probe, and SettingsDrawer", () =>
                {
                    string[] files =
                    {
                        Path.Combine(sourceDir, "Client", "ModelServiceClient.cs"),
                        Path.Combine(sourceDir, "Diagnostics", "EndpointHealthProbe.cs"),
                        Path.Combine(sourceDir, "Settings", "ModelServiceSettingsDrawer.cs")
                    };
                    foreach (var file in files)
                    {
                        string code = StripComments(File.ReadAllText(file));
                        Assert.DoesNotContain("ProviderType.LocalSubscriptionGateway", code, StringComparison.Ordinal);
                    }

                    string drawerCode = StripComments(File.ReadAllText(Path.Combine(sourceDir, "Settings", "ModelServiceSettingsDrawer.cs")));
                    Assert.DoesNotContain("OpenCodeGoProtocolId", drawerCode, StringComparison.Ordinal);
                    Assert.DoesNotContain("DeepSeekProtocolId", drawerCode, StringComparison.Ordinal);
                }),
                ("zero redundant static HttpClient instantiations across client, probe, and audio services", () =>
                {
                    string[] files =
                    {
                        Path.Combine(sourceDir, "Client", "ModelServiceClient.cs"),
                        Path.Combine(sourceDir, "Diagnostics", "EndpointHealthProbe.cs"),
                        Path.Combine(sourceDir, "Audio", "DeepSeekSpeechSynthesizer.cs"),
                        Path.Combine(sourceDir, "Audio", "DeepSeekSpeechRecognizer.cs")
                    };
                    foreach (var file in files)
                    {
                        string code = StripComments(File.ReadAllText(file));
                        Assert.DoesNotContain("new HttpClient", code, StringComparison.Ordinal);
                        Assert.Contains("RimMindHttpClientPool", code, StringComparison.Ordinal);
                    }
                }),
                ("SettingsDrawer and DebugActions have zero raw .Translate() calls or local TranslateOrFallback and use shared SettingsUIDrawer", () =>
                {
                    string drawerCode = StripComments(File.ReadAllText(Path.Combine(sourceDir, "Settings", "ModelServiceSettingsDrawer.cs")));
                    Assert.DoesNotContain(".Translate(", drawerCode, StringComparison.Ordinal);
                    Assert.DoesNotContain("TranslateOrFallback", drawerCode, StringComparison.Ordinal);
                    Assert.Contains("SettingsUIDrawer.DrawScrollableSettingsPage", drawerCode, StringComparison.Ordinal);
                    Assert.Contains("SettingsUIDrawer.DrawSecretField", drawerCode, StringComparison.Ordinal);
                    Assert.Contains("onDirtySave:", drawerCode, StringComparison.Ordinal);

                    string debugCode = StripComments(File.ReadAllText(Path.Combine(sourceDir, "Debug", "ModelServiceDebugActions.cs")));
                    Assert.DoesNotContain(".Translate(", debugCode, StringComparison.Ordinal);
                }),
                ("ModelProtocolRegistry is split from concrete protocol implementations", () =>
                {
                    string registryCode = StripComments(File.ReadAllText(Path.Combine(sourceDir, "Protocol", "ModelProtocolRegistry.cs")));
                    Assert.DoesNotContain("class OpenAIModelProtocol", registryCode, StringComparison.Ordinal);
                    Assert.DoesNotContain("class AnthropicModelProtocol", registryCode, StringComparison.Ordinal);
                    Assert.DoesNotContain("class DeepSeekModelProtocol", registryCode, StringComparison.Ordinal);
                    Assert.DoesNotContain("ApplyOpenCodeSessionHeader", registryCode, StringComparison.Ordinal);
                }));
        }

        private static string FindModelServiceSourceDir()
        {
            string? dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "Source", "RimMindModelService.csproj");
                if (File.Exists(candidate))
                    return Path.Combine(dir, "Source");

                string nested = Path.Combine(dir, "RimMind-Extension-ModelService", "Source", "RimMindModelService.csproj");
                if (File.Exists(nested))
                    return Path.Combine(dir, "RimMind-Extension-ModelService", "Source");

                dir = Directory.GetParent(dir)?.FullName;
            }
            return string.Empty;
        }

        private static string StripComments(string source) =>
            Regex.Replace(source, @"//[^\r\n]*|/\*.*?\*/", " ", RegexOptions.Singleline | RegexOptions.CultureInvariant);

        private sealed class CustomLoopbackOnlyProtocol : OpenAIModelProtocol
        {
            private static readonly ProtocolCapabilities LoopbackCaps = new ProtocolCapabilities(requiresLoopback: true);

            public override string ProtocolId => "custom_loopback_only";
            public override ProviderType LegacyProviderType => ProviderType.OpenAICompatible;
            public override string FallbackLabel => "Custom Loopback Only";
            public override ProtocolCapabilities Capabilities => LoopbackCaps;

            public override Uri ResolveCompletionUri(ModelEndpointConfig endpoint)
            {
                string baseUrl = endpoint.endpoint.Trim().TrimEnd('/');
                return new Uri(baseUrl + "/custom/complete");
            }
        }
    }
}
