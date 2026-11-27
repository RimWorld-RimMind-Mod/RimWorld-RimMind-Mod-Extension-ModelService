using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Llm;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;
using RimMind.Testing;
using Xunit;

namespace RimMind.ModelService.Tests.Contracts
{
    public sealed class MultimodalVisionProtocolContracts
    {
        private const string SamplePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+X2dIAAAAASUVORK5CYII=";

        private static LlmRequestEnvelope BuildMultimodalUserEnvelope(
            string prompt = "Inspect the map and pick a bedroom spot.",
            string? mediaType = "image/png",
            string? detail = "high",
            string? label = "Map View r=12",
            string? jsonSchema = null)
        {
            return new LlmRequestEnvelope
            {
                RequestId = "req-vision-1",
                MaxTokens = 256,
                Temperature = 0.2f,
                JsonSchema = jsonSchema,
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "system", Content = "You are a colony architect.", LayerTag = "L0" },
                    new ChatMessage
                    {
                        Role = "user",
                        Content = prompt,
                        Attachments = new List<LlmImageAttachment>
                        {
                            new LlmImageAttachment
                            {
                                MediaType = mediaType ?? string.Empty,
                                Base64Data = SamplePngBase64,
                                Label = label,
                                Detail = detail
                            }
                        }
                    }
                }
            };
        }

        [Fact]
        public void BuiltIn_protocols_declare_SupportsVision_capability_while_default_stays_opt_in()
        {
            ContractCaseRunner.Run(
                ("ProtocolCapabilities.Default opts out of vision until explicitly enabled", () =>
                {
                    Assert.False(ProtocolCapabilities.Default.SupportsVision);
                }),
                ("all five built-in protocols advertise SupportsVision = true", () =>
                {
                    string[] visionProtocolIds =
                    {
                        ModelProtocolRegistry.OpenAIProtocolId,
                        ModelProtocolRegistry.OpenCodeGoProtocolId,
                        ModelProtocolRegistry.DeepSeekProtocolId,
                        ModelProtocolRegistry.AnthropicProtocolId,
                        ModelProtocolRegistry.LocalGatewayProtocolId
                    };

                    foreach (string id in visionProtocolIds)
                    {
                        var protocol = ModelProtocolRegistry.Resolve(id);
                        Assert.True(protocol.Capabilities.SupportsVision, $"Expected protocol '{id}' to support vision.");
                    }
                }));
        }

        [Fact]
        public void OpenAI_and_OpenCodeGo_serialize_image_url_parts_and_preserve_plain_text()
        {
            ContractCaseRunner.Run(
                ("OpenAIProtocolAdapter and OpenCodeGoModelProtocol emit data-URI image_url blocks for attachments", () =>
                {
                    var envelope = BuildMultimodalUserEnvelope();
                    var openAiNode = new ModelEndpointConfig { modelName = "gpt-4o-mini", protocolId = ModelProtocolRegistry.OpenAIProtocolId };
                    string openAiJson = OpenAIProtocolAdapter.BuildRequestJson(envelope, openAiNode);
                    var openAiRoot = JObject.Parse(openAiJson);
                    var openAiContent = openAiRoot["messages"]?[1]?["content"] as JArray;

                    Assert.NotNull(openAiContent);
                    Assert.Equal(2, openAiContent!.Count);
                    Assert.Equal("text", openAiContent[0]?["type"]?.ToString());
                    Assert.Contains("Inspect the map", openAiContent[0]?["text"]?.ToString());
                    Assert.Equal("image_url", openAiContent[1]?["type"]?.ToString());
                    Assert.Equal(
                        $"data:image/png;base64,{SamplePngBase64}",
                        openAiContent[1]?["image_url"]?["url"]?.ToString());
                    Assert.Equal("high", openAiContent[1]?["image_url"]?["detail"]?.ToString());

                    var openCodeNode = new ModelEndpointConfig
                    {
                        modelName = "deepseek-v4.1-flash",
                        protocolId = ModelProtocolRegistry.OpenCodeGoProtocolId
                    };
                    var openCodeRes = OpenCodeGoModelProtocol.Instance.BuildRequestJson(envelope, openCodeNode, openCodeNode.modelName);
                    Assert.True(openCodeRes.IsOk);
                    var openCodeRoot = JObject.Parse(openCodeRes.Value);
                    var openCodeContent = openCodeRoot["messages"]?[1]?["content"] as JArray;
                    Assert.NotNull(openCodeContent);
                    Assert.Equal("image_url", openCodeContent![1]?["type"]?.ToString());
                    Assert.Equal(
                        $"data:image/png;base64,{SamplePngBase64}",
                        openCodeContent[1]?["image_url"]?["url"]?.ToString());
                }),
                ("plain-text requests stay plain string content and allowVisionAttachments=false degrades cleanly", () =>
                {
                    var plainEnv = new LlmRequestEnvelope
                    {
                        MaxTokens = 128,
                        Temperature = 0.5f,
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", Content = "Rules" },
                            new ChatMessage { Role = "user", Content = "Status?" }
                        }
                    };
                    var node = new ModelEndpointConfig { modelName = "gpt-4o-mini" };
                    string adapterJson = OpenAIProtocolAdapter.BuildRequestJson(plainEnv, node);
                    string codecJson = OpenAIWireCodec.BuildRequestJson(plainEnv, "gpt-4o-mini", defaultMaxTokens: 0, omitZeroMaxTokens: true);
                    Assert.Equal(codecJson, adapterJson);

                    var plainParsed = JObject.Parse(adapterJson);
                    Assert.Equal(JTokenType.String, plainParsed["messages"]?[1]?["content"]?.Type);

                    var visionEnv = BuildMultimodalUserEnvelope();
                    string degradedJson = OpenAIProtocolAdapter.BuildRequestJson(visionEnv, node, allowVisionAttachments: false);
                    var degradedRoot = JObject.Parse(degradedJson);
                    Assert.Equal(JTokenType.String, degradedRoot["messages"]?[1]?["content"]?.Type);
                    Assert.Equal("Inspect the map and pick a bedroom spot.", degradedRoot["messages"]?[1]?["content"]?.ToString());

                    var textOnlyProto = new TextOnlyOpenAISubclass();
                    var subRes = textOnlyProto.BuildRequestJson(visionEnv, node, "custom-text-model");
                    Assert.True(subRes.IsOk);
                    var subRoot = JObject.Parse(subRes.Value);
                    Assert.Equal(JTokenType.String, subRoot["messages"]?[1]?["content"]?.Type);
                }));
        }

        [Fact]
        public void DeepSeekProtocolAdapter_emits_image_url_for_flash_models_and_degrades_pro_to_plain_text()
        {
            ContractCaseRunner.Run(
                ("deepseek-flash and deepseek-v4.1-flash serialize image_url parts and preserve json_object keyword guard", () =>
                {
                    var envelope = BuildMultimodalUserEnvelope(
                        prompt: "Pick candidate id.",
                        jsonSchema: "{\"type\":\"object\",\"properties\":{\"candidate_id\":{\"type\":\"integer\"}}}");

                    foreach (string model in new[] { "deepseek-flash", "deepseek-v4.1-flash" })
                    {
                        var node = new ModelEndpointConfig { modelName = model, safetyLevel = EndpointSafetyLevel.Standard };
                        var root = JObject.Parse(DeepSeekProtocolAdapter.BuildRequestJson(envelope, node));

                        Assert.Equal(model, root["model"]?.ToString());
                        Assert.Equal("json_object", root["response_format"]?["type"]?.ToString());
                        Assert.Contains("json", root["messages"]?[0]?["content"]?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

                        var userContent = root["messages"]?[1]?["content"] as JArray;
                        Assert.NotNull(userContent);
                        Assert.Equal("image_url", userContent![1]?["type"]?.ToString());
                        Assert.Equal(
                            $"data:image/png;base64,{SamplePngBase64}",
                            userContent[1]?["image_url"]?["url"]?.ToString());
                    }
                }),
                ("pure-text deepseek-v4-pro degrades multimodal user turn cleanly to plain string content", () =>
                {
                    var envelope = BuildMultimodalUserEnvelope(prompt: "Evaluate defense plan.");
                    var proNode = new ModelEndpointConfig { modelName = "deepseek-v4-pro", safetyLevel = EndpointSafetyLevel.High };

                    var root = JObject.Parse(DeepSeekProtocolAdapter.BuildRequestJson(envelope, proNode));
                    Assert.Equal("deepseek-v4-pro", root["model"]?.ToString());
                    var userContent = root["messages"]?[1]?["content"];
                    Assert.NotNull(userContent);
                    Assert.Equal(JTokenType.String, userContent!.Type);
                    Assert.Equal("Evaluate defense plan.", userContent.ToString());
                    Assert.DoesNotContain(SamplePngBase64, root.ToString());
                }));
        }

        [Fact]
        public void AnthropicProtocolAdapter_emits_native_base64_image_blocks_including_merged_tool_turns()
        {
            ContractCaseRunner.Run(
                ("user message attachments serialize as Anthropic base64 image blocks with image/png default and skip empty/assistant attachments", () =>
                {
                    var envelope = new LlmRequestEnvelope
                    {
                        MaxTokens = 512,
                        Temperature = 0.3f,
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", Content = "Architect rules", LayerTag = "L0" },
                            new ChatMessage
                            {
                                Role = "user",
                                Content = "Inspect room layout.",
                                Attachments = new List<LlmImageAttachment>
                                {
                                    new LlmImageAttachment { MediaType = "", Base64Data = SamplePngBase64 },
                                    new LlmImageAttachment { MediaType = "image/jpeg", Base64Data = "   " },
                                    null!
                                }
                            },
                            new ChatMessage
                            {
                                Role = "assistant",
                                Content = "Noted.",
                                Attachments = new List<LlmImageAttachment>
                                {
                                    new LlmImageAttachment { MediaType = "image/png", Base64Data = SamplePngBase64 }
                                }
                            }
                        }
                    };

                    var node = new ModelEndpointConfig { modelName = "claude-3-5-sonnet-20241022" };
                    var root = JObject.Parse(AnthropicProtocolAdapter.BuildRequestJson(envelope, node));
                    var messages = root["messages"] as JArray;
                    Assert.NotNull(messages);
                    Assert.Equal(2, messages!.Count);

                    var userBlocks = messages[0]?["content"] as JArray;
                    Assert.NotNull(userBlocks);
                    Assert.Equal(2, userBlocks!.Count);
                    Assert.Equal("text", userBlocks[0]?["type"]?.ToString());
                    Assert.Equal("Inspect room layout.", userBlocks[0]?["text"]?.ToString());
                    Assert.Equal("image", userBlocks[1]?["type"]?.ToString());
                    Assert.Equal("base64", userBlocks[1]?["source"]?["type"]?.ToString());
                    Assert.Equal("image/png", userBlocks[1]?["source"]?["media_type"]?.ToString());
                    Assert.Equal(SamplePngBase64, userBlocks[1]?["source"]?["data"]?.ToString());

                    // Assistant messages must never emit image blocks.
                    var assistantBlocks = messages[1]?["content"] as JArray;
                    Assert.NotNull(assistantBlocks);
                    Assert.Single(assistantBlocks!);
                    Assert.Equal("text", assistantBlocks[0]?["type"]?.ToString());
                }),
                ("consecutive tool_result and multimodal user turn merge cleanly into one alternating Anthropic user message", () =>
                {
                    var envelope = new LlmRequestEnvelope
                    {
                        MaxTokens = 512,
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage
                            {
                                Role = "assistant",
                                Content = "Inspecting map view.",
                                ToolCalls = new List<ChatToolCall>
                                {
                                    new ChatToolCall { Id = "toolu_view_1", Name = "actions_inspect_map_view", Arguments = "{\"radius\":12}" }
                                }
                            },
                            new ChatMessage
                            {
                                Role = "tool",
                                ToolCallId = "toolu_view_1",
                                Content = "{\"candidates\":[1,2]}"
                            },
                            new ChatMessage
                            {
                                Role = "user",
                                Content = "[Attached visual inspection view]",
                                Attachments = new List<LlmImageAttachment>
                                {
                                    new LlmImageAttachment { MediaType = "image/jpeg", Base64Data = SamplePngBase64 }
                                }
                            }
                        }
                    };

                    var res = AnthropicModelProtocol.Instance.BuildRequestJson(
                        envelope,
                        new ModelEndpointConfig { modelName = "claude-3-5-haiku-20241022" },
                        "claude-3-5-haiku-20241022");
                    Assert.True(res.IsOk);

                    var root = JObject.Parse(res.Value);
                    var messages = root["messages"] as JArray;
                    Assert.NotNull(messages);
                    Assert.Equal(2, messages!.Count);
                    Assert.Equal("assistant", messages[0]?["role"]?.ToString());
                    Assert.Equal("user", messages[1]?["role"]?.ToString());

                    var mergedBlocks = messages[1]?["content"] as JArray;
                    Assert.NotNull(mergedBlocks);
                    Assert.Equal(3, mergedBlocks!.Count);
                    Assert.Equal("tool_result", mergedBlocks[0]?["type"]?.ToString());
                    Assert.Equal("toolu_view_1", mergedBlocks[0]?["tool_use_id"]?.ToString());
                    Assert.Equal("text", mergedBlocks[1]?["type"]?.ToString());
                    Assert.Equal("image", mergedBlocks[2]?["type"]?.ToString());
                    Assert.Equal("base64", mergedBlocks[2]?["source"]?["type"]?.ToString());
                    Assert.Equal("image/jpeg", mergedBlocks[2]?["source"]?["media_type"]?.ToString());
                    Assert.Equal(SamplePngBase64, mergedBlocks[2]?["source"]?["data"]?.ToString());
                }));
        }

        private sealed class TextOnlyOpenAISubclass : OpenAIModelProtocol
        {
            private static readonly ProtocolCapabilities TextOnlyCaps = new ProtocolCapabilities(supportsVision: false);

            public override string ProtocolId => "text_only_openai_test";
            public override ProtocolCapabilities Capabilities => TextOnlyCaps;
        }
    }
}
