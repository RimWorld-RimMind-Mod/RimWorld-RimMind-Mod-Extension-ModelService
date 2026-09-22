using System;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.ModelService.Models;
using RimMind.ModelService.Settings;

namespace RimMind.ModelService.Client
{
    public class OpenCodeGoClientFactory : IAIClientFactory
    {
        public const string OpenCodeProviderId = "opencode";

        public string Id => OpenCodeProviderId;
        public string OwnerModId => "mcocdaa.RimMindModelService";
        public string ProviderId => OpenCodeProviderId;
        public bool RequiresApiKey => true;

        public string DisplayLabel => global::Verse.Translator.Translate("RimMind.ModelService.Provider.OpenCodeGo");
        public string? DefaultEndpoint => "https://opencode.ai/zen/go/v1";
        public string? DefaultModelName => "deepseek-v4.1-flash";
        public int OrderWeight => 40;
        public bool VisibleInMenu => true;

        public IAIClient Create(ISettingsProvider settings)
        {
            string endpointUrl = !string.IsNullOrWhiteSpace(settings?.ApiEndpoint)
                ? settings!.ApiEndpoint
                : "https://opencode.ai/zen/go/v1";
            string model = !string.IsNullOrWhiteSpace(settings?.ModelName)
                ? settings!.ModelName
                : "deepseek-v4.1-flash";
            string apiKey = settings?.ApiKey ?? string.Empty;

            var node = new ModelEndpointConfig
            {
                id = "opencode_direct",
                name = "OpenCode Go (Direct)",
                endpoint = endpointUrl,
                modelName = model,
                apiKey = apiKey,
                providerType = ProviderType.OpenCodeGo,
                isEnabled = !string.IsNullOrWhiteSpace(apiKey),
                priority = 0,
                weight = 1
            };

            var endpoints = new[] { node };
            return new ModelServiceClient(
                () => endpoints,
                () => BalancingStrategy.PriorityFailover);
        }
    }
}

