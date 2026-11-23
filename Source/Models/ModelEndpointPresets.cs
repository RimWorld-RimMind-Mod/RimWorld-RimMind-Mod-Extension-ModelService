using System;
using RimMind.ModelService.Protocol;

namespace RimMind.ModelService.Models
{
    public static class ModelEndpointPresets
    {
        public static ModelEndpointConfig CreatePreset(string protocolId, string? displayName = null, int priority = 0)
        {
            var protocol = ModelProtocolRegistry.Resolve(protocolId);
            if (string.IsNullOrWhiteSpace(displayName) && protocol.PresetDescriptor != null)
            {
                return protocol.PresetDescriptor.CreateEndpoint(priority);
            }

            return new ModelEndpointConfig
            {
                id = Guid.NewGuid().ToString("N"),
                name = !string.IsNullOrWhiteSpace(displayName) ? displayName! : protocol.FallbackLabel,
                endpoint = protocol.DefaultEndpointUrl,
                modelName = protocol.DefaultModel,
                protocolId = protocol.ProtocolId,
                isEnabled = true,
                priority = priority,
                weight = 1
            };
        }

        public static ModelEndpointConfig CreateCodexGatewayPreset(int priority = 0) =>
            LocalGatewayModelProtocol.Instance.PresetDescriptor!.CreateEndpoint(priority);

        public static ModelEndpointConfig CreateOpenCodeGoPreset(int priority = 1) =>
            OpenCodeGoModelProtocol.Instance.PresetDescriptor!.CreateEndpoint(priority);

        public static ModelEndpointConfig CreateDeepSeekPreset(int priority = 0) =>
            DeepSeekModelProtocol.Instance.PresetDescriptor!.CreateEndpoint(priority);
    }
}
