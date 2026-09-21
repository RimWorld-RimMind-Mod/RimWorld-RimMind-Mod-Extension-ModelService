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
            name = "OpenCode Go Gateway (sub2api)",
            endpoint = "http://127.0.0.1:8080/v1",
            modelName = "claude-3-5-sonnet-20241022",
            providerType = ProviderType.LocalSubscriptionGateway,
            isEnabled = true,
            priority = priority,
            weight = 1
        };
    }
}
