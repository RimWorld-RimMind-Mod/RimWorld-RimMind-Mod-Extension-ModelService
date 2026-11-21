using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    public sealed class LocalGatewayModelProtocol : OpenAIModelProtocol
    {
        public static new readonly LocalGatewayModelProtocol Instance = new LocalGatewayModelProtocol();

        private static readonly ProtocolCapabilities ProtocolCaps = new ProtocolCapabilities(
            supportsNativeThinking: false,
            supportsSafetyCheck: false,
            supportsJsonSchema: true,
            requiresReasoningContentInToolHistory: false,
            requiresLoopback: true);

        public override string ProtocolId => ModelProtocolRegistry.LocalGatewayProtocolId;
        public override ProviderType LegacyProviderType => ProviderType.LocalSubscriptionGateway;
        public override string DisplayLabelKey => "RimMind.ModelService.Provider.LocalSubscriptionGateway";
        public override string FallbackLabel => "Local Gateway (sub2api)";
        public override string DefaultEndpointUrl => "http://127.0.0.1:8000/v1";
        public override string DefaultModel => "gpt-4o";
        public override ProtocolCapabilities Capabilities => ProtocolCaps;
    }
}
