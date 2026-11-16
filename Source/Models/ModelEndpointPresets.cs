using System;
using RimMind.ModelService.Protocol;

namespace RimMind.ModelService.Models
{
    public static class ModelEndpointPresets
    {
        public static ModelEndpointConfig CreatePreset(string protocolId, string? displayName = null, int priority = 0)
        {
            var protocol = ModelProtocolRegistry.Resolve(protocolId);
            return new ModelEndpointConfig
            {
                id = Guid.NewGuid().ToString("N"),
                name = !string.IsNullOrWhiteSpace(displayName) ? displayName! : protocol.FallbackLabel,
                endpoint = protocol.DefaultEndpointUrl,
                modelName = protocol.DefaultModel,
                providerType = protocol.LegacyProviderType,
                protocolId = protocol.ProtocolId,
                isEnabled = true,
                priority = priority,
                weight = 1
            };
        }

        public static ModelEndpointConfig CreateCodexGatewayPreset(int priority = 0) =>
            CreatePreset(ModelProtocolRegistry.LocalGatewayProtocolId, "Codex Local Gateway (sub2api)", priority);

        public static ModelEndpointConfig CreateOpenCodeGoPreset(int priority = 1) =>
            CreatePreset(ModelProtocolRegistry.OpenCodeGoProtocolId, "OpenCode Go (Direct)", priority);

        public static ModelEndpointConfig CreateDeepSeekPreset(int priority = 0)
        {
            var cfg = CreatePreset(ModelProtocolRegistry.DeepSeekProtocolId, "DeepSeek Official", priority);
            cfg.timeoutSeconds = 60;
            cfg.isEnabled = false;
            cfg.thinkingPolicy = EndpointThinkingPolicy.FollowRequest;
            cfg.safetyLevel = EndpointSafetyLevel.High;
            return cfg;
        }
    }
}
