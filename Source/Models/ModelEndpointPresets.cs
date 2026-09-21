using System;

namespace RimMind.ModelService.Models
{
    public static class ModelEndpointPresets
    {
        public static ModelEndpointConfig CreateCodexGatewayPreset(int priority = 0) => new ModelEndpointConfig
        {
            id = Guid.NewGuid().ToString("N"),
            name = "Codex Local Gateway (sub2api)",
            endpoint = "http://127.0.0.1:8000/v1",
            modelName = "gpt-4o",
            providerType = ProviderType.LocalSubscriptionGateway,
            isEnabled = true,
            priority = priority,
            weight = 1
        };

        public static ModelEndpointConfig CreateOpenCodeGoPreset(int priority = 1) => new ModelEndpointConfig
        {
            id = Guid.NewGuid().ToString("N"),
            name = "OpenCode Go (Direct)",
            endpoint = "https://opencode.ai/zen/go/v1",
            modelName = "deepseek-v4.1-flash",
            providerType = ProviderType.OpenCodeGo,
            isEnabled = true,
            priority = priority,
            weight = 1
        };
    }
}
