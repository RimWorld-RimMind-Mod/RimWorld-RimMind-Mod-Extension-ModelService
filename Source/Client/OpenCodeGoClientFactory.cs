using System;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;
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

        public string DisplayLabel => global::Verse.Translator.CanTranslate("RimMind.ModelService.Provider.OpenCodeGo")
            ? (string)global::Verse.Translator.Translate("RimMind.ModelService.Provider.OpenCodeGo")
            : "OpenCode Go";
        public string? DefaultEndpoint => OpenCodeGoModelProtocol.Instance.DefaultEndpointUrl;
        public string? DefaultModelName => OpenCodeGoModelProtocol.Instance.DefaultModel;
        public int OrderWeight => 40;
        public bool VisibleInMenu => true;

        public IAIClient Create(ISettingsProvider settings)
        {
            var protocol = ModelProtocolRegistry.Resolve(ModelProtocolRegistry.OpenCodeGoProtocolId);
            string endpointUrl = !string.IsNullOrWhiteSpace(settings?.ApiEndpoint)
                ? settings!.ApiEndpoint
                : protocol.DefaultEndpointUrl;
            string model = !string.IsNullOrWhiteSpace(settings?.ModelName)
                ? settings!.ModelName
                : protocol.DefaultModel;
            string apiKey = settings?.ApiKey ?? string.Empty;

            var node = new ModelEndpointConfig
            {
                id = "opencode_direct",
                name = "OpenCode Go (Direct)",
                endpoint = endpointUrl,
                modelName = model,
                apiKey = apiKey,
                providerType = protocol.LegacyProviderType,
                protocolId = protocol.ProtocolId,
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
