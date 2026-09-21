using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.ModelService.Settings;

namespace RimMind.ModelService.Client
{
    public class ModelServiceClientFactory : IAIClientFactory
    {
        public const string ModelServiceProviderId = "extended_service";

        public string Id => ModelServiceProviderId;
        public string OwnerModId => "mcocdaa.RimMindModelService";
        public string ProviderId => ModelServiceProviderId;
        public bool RequiresApiKey => false;

        public IAIClient Create(ISettingsProvider settings)
        {
            var msSettings = RimMindModelServiceMod.Settings;
            return new ModelServiceClient(
                () => msSettings.endpoints,
                () => msSettings.balancingStrategy);
        }
    }
}
