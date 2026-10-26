using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.ModelService.Models;
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

        public string DisplayLabel => global::Verse.Translator.CanTranslate("RimMind.ModelService.Provider.ExtendedService")
            ? (string)global::Verse.Translator.Translate("RimMind.ModelService.Provider.ExtendedService")
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
                () =>
                {
                    if (msSettings == null || !msSettings.enableService || msSettings.endpoints == null)
                        return Array.Empty<ModelEndpointConfig>();
                    return envOverlay == null
                        ? msSettings.endpoints
                        : envOverlay.Apply(msSettings.endpoints);
                },
                () => msSettings?.balancingStrategy ?? BalancingStrategy.PriorityFailover);
        }
    }
}
