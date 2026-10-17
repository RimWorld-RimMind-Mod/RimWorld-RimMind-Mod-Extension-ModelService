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

        public string DisplayLabel => global::Verse.Translator.Translate("RimMind.ModelService.Provider.ExtendedService");
        public string? DefaultEndpoint => null;
        public string? DefaultModelName => null;
        public int OrderWeight => 50;
        public bool VisibleInMenu => true;

        public IAIClient Create(ISettingsProvider settings)
        {
            var msSettings = RimMindModelServiceMod.Settings;
            string? testKey = Environment.GetEnvironmentVariable("RIMMIND_TEST_API_KEY");
            string? backupKey = Environment.GetEnvironmentVariable("RIMMIND_BACKUP_API_KEY");
            string? effectiveOpenCodeKey = !string.IsNullOrWhiteSpace(backupKey) ? backupKey : testKey;
            if (!string.IsNullOrEmpty(effectiveOpenCodeKey) && msSettings?.endpoints != null)
            {
                string? backupEndpoint = Environment.GetEnvironmentVariable("RIMMIND_BACKUP_ENDPOINT")
                    ?? Environment.GetEnvironmentVariable("RIMMIND_TEST_ENDPOINT");
                string? backupModel = Environment.GetEnvironmentVariable("RIMMIND_BACKUP_MODEL")
                    ?? Environment.GetEnvironmentVariable("RIMMIND_TEST_MODEL");
                foreach (var ep in msSettings.endpoints)
                {
                    if (string.IsNullOrEmpty(ep.apiKey) && ep.providerType == ProviderType.OpenCodeGo)
                    {
                        ep.apiKey = effectiveOpenCodeKey!;
                        if (!string.IsNullOrEmpty(backupEndpoint)) ep.endpoint = backupEndpoint!;
                        if (!string.IsNullOrEmpty(backupModel)) ep.modelName = backupModel!;
                    }
                }
            }

            return new ModelServiceClient(
                () => msSettings?.endpoints ?? (IReadOnlyList<ModelEndpointConfig>)Array.Empty<ModelEndpointConfig>(),
                () => msSettings?.balancingStrategy ?? BalancingStrategy.PriorityFailover);
        }
    }
}
