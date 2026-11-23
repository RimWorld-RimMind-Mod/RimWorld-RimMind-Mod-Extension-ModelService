using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Client
{
    public class ModelServiceClientFactory : IAIClientFactory
    {
        public const string ModelServiceProviderId = "extended_service";
        public const string ModelServiceSettingsTabId = "model_service";
        public const string ModelServiceDisplayLabelKey = "RimMind.ModelService.Provider.ExtendedService";
        public const string ModelServiceOwnerModId = "mcocdaa.RimMindModelService";

        static ModelServiceClientFactory()
        {
            RegisterDescriptor();
        }

        public ModelServiceClientFactory()
        {
            RegisterDescriptor();
        }

        public static void RegisterDescriptor()
        {
            AIProviderRegistry.Register(new AIProviderDescriptor(
                ModelServiceProviderId,
                RequiresApiKey: false,
                SettingsTabId: ModelServiceSettingsTabId,
                DisplayLabelKey: ModelServiceDisplayLabelKey,
                OwnerModId: ModelServiceOwnerModId));
        }

        public string Id => ModelServiceProviderId;
        public string OwnerModId => ModelServiceOwnerModId;
        public string ProviderId => ModelServiceProviderId;
        public bool RequiresApiKey => false;

        public string DisplayLabel => global::Verse.Translator.CanTranslate(ModelServiceDisplayLabelKey)
            ? (string)global::Verse.Translator.Translate(ModelServiceDisplayLabelKey)
            : "Extended Model Service (Load Balanced)";
        public string? DefaultEndpoint => null;
        public string? DefaultModelName => null;
        public int OrderWeight => 50;
        public bool VisibleInMenu => true;

        public IAIClient Create(ISettingsProvider settings)
        {
            var msSettings = RimMindModelServiceMod.Settings;
            // Automation credentials are overlaid on shadow copies; persisted settings are never mutated.
            var envOverlay = EnvironmentEndpointOverlay.FromEnvironment();

            return new ModelServiceClient(
                () => EndpointPoolResolver.GetEndpoints(
                    msSettings,
                    envOverlay,
                    protocolIdFilter: null,
                    requireEnableService: true,
                    enabledOnly: false),
                () => msSettings?.balancingStrategy ?? BalancingStrategy.PriorityFailover);
        }
    }
}
