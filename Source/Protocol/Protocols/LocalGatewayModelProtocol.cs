using System;
using System.Collections.Generic;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    public sealed class LocalGatewayModelProtocol : OpenAIModelProtocol
    {
        public static new readonly LocalGatewayModelProtocol Instance = new LocalGatewayModelProtocol();

        private static readonly string[] Aliases = { nameof(ProviderType.LocalSubscriptionGateway) };

        private static readonly ProtocolCapabilities ProtocolCaps = new ProtocolCapabilities(
            supportsNativeThinking: false,
            supportsSafetyCheck: false,
            supportsJsonSchema: true,
            requiresReasoningContentInToolHistory: false,
            requiresLoopback: true,
            supportsVision: true);

        private static readonly ProtocolPresetDescriptor Preset = new ProtocolPresetDescriptor(
            buttonLabelKey: "RimMind.ModelService.Settings.AddCodexPreset",
            buttonLabelFallback: "+ Codex Preset",
            tooltipKey: "RimMind.ModelService.Settings.AddCodexPreset.Desc",
            tooltipFallback: "Quickly add local Codex gateway endpoint preset.",
            buttonWidth: 140f,
            createEndpoint: priority => new ModelEndpointConfig
            {
                id = Guid.NewGuid().ToString("N"),
                name = "Codex Local Gateway (sub2api)",
                endpoint = Instance.DefaultEndpointUrl,
                modelName = Instance.DefaultModel,
                protocolId = ModelProtocolRegistry.LocalGatewayProtocolId,
                providerType = ProviderType.LocalSubscriptionGateway,
                isEnabled = true,
                priority = priority,
                weight = 1
            });

        public override string ProtocolId => ModelProtocolRegistry.LocalGatewayProtocolId;
        public override ProviderType LegacyProviderType => ProviderType.LocalSubscriptionGateway;
        public override IReadOnlyList<string> LegacyAliases => Aliases;
        public override string DisplayLabelKey => "RimMind.ModelService.Provider.LocalSubscriptionGateway";
        public override string FallbackLabel => "Local Gateway (sub2api)";
        public override string DefaultEndpointUrl => "http://127.0.0.1:8000/v1";
        public override string DefaultModel => "gpt-4o";
        public override ProtocolCapabilities Capabilities => ProtocolCaps;
        public override ProtocolPresetDescriptor? PresetDescriptor => Preset;
    }
}
