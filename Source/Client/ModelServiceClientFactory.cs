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

        public IAIClient Create(ISettingsProvider settings)
        {
            var msSettings = RimMindModelServiceMod.Settings;
            string? testKey = Environment.GetEnvironmentVariable("RIMMIND_TEST_API_KEY");
            if (!string.IsNullOrEmpty(testKey) && msSettings?.endpoints != null)
            {
                string? testEndpoint = Environment.GetEnvironmentVariable("RIMMIND_TEST_ENDPOINT");
                string? testModel = Environment.GetEnvironmentVariable("RIMMIND_TEST_MODEL");
                foreach (var ep in msSettings.endpoints)
                {
                    if (string.IsNullOrEmpty(ep.apiKey) && ep.providerType == ProviderType.OpenCodeGo)
                    {
                        ep.apiKey = testKey;
                        if (!string.IsNullOrEmpty(testEndpoint)) ep.endpoint = testEndpoint;
                        if (!string.IsNullOrEmpty(testModel)) ep.modelName = testModel;
                    }
                }
            }

            IReadOnlyList<ModelEndpointConfig> endpoints = msSettings?.endpoints ?? (IReadOnlyList<ModelEndpointConfig>)Array.Empty<ModelEndpointConfig>();
            BalancingStrategy strategy = msSettings?.balancingStrategy ?? BalancingStrategy.PriorityFailover;

            return new ModelServiceClient(
                () => endpoints,
                () => strategy);
        }
    }
}
