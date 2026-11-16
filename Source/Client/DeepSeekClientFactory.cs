using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;
using RimMind.ModelService.Security;

namespace RimMind.ModelService.Client
{
    /// <summary>
    /// Built-in DeepSeek official protocol provider factory registered into Core via <see cref="IAIClientFactory"/>.
    /// Delegates to enabled <c>deepseek</c> nodes in ModelServiceSettings if configured, or builds an isolated
    /// DeepSeek node from Core's active <see cref="ISettingsProvider"/> credentials.
    /// </summary>
    public class DeepSeekClientFactory : IAIClientFactory
    {
        public const string DeepSeekProviderId = "deepseek_native";

        public string Id => DeepSeekProviderId;
        public string OwnerModId => ModelServiceClientFactory.ModelServiceOwnerModId;
        public string ProviderId => DeepSeekProviderId;
        public bool RequiresApiKey => true;

        public string DisplayLabel => global::Verse.Translator.CanTranslate("RimMind.ModelService.Provider.DeepSeekFactory")
            ? (string)global::Verse.Translator.Translate("RimMind.ModelService.Provider.DeepSeekFactory")
            : "DeepSeek Official (V4 / Thinking / Safety)";

        public string? DefaultEndpoint => DeepSeekModelProtocol.Instance.DefaultEndpointUrl;
        public string? DefaultModelName => DeepSeekModelProtocol.Instance.DefaultModel;
        public int OrderWeight => 35;
        public bool VisibleInMenu => true;

        public IAIClient Create(ISettingsProvider settings)
        {
            var protocol = ModelProtocolRegistry.Resolve(ModelProtocolRegistry.DeepSeekProtocolId);
            var msSettings = RimMindModelServiceMod.Settings;
            var envOverlay = EnvironmentEndpointOverlay.FromEnvironment();

            string coreApiKey = settings?.ApiKey ?? string.Empty;
            string? coreEndpoint = settings?.ApiEndpoint;
            string endpointUrl = !string.IsNullOrWhiteSpace(coreEndpoint) && IsDeepSeekOrLoopbackEndpoint(coreEndpoint!)
                ? coreEndpoint!
                : protocol.DefaultEndpointUrl;
            string model = !string.IsNullOrWhiteSpace(settings?.ModelName)
                ? settings!.ModelName
                : protocol.DefaultModel;

            var directNode = new ModelEndpointConfig
            {
                id = "deepseek_direct",
                name = "DeepSeek Official (Direct)",
                endpoint = endpointUrl,
                modelName = model,
                apiKey = coreApiKey,
                providerType = ProviderType.DeepSeekOfficial,
                protocolId = ModelProtocolRegistry.DeepSeekProtocolId,
                thinkingPolicy = EndpointThinkingPolicy.FollowRequest,
                safetyLevel = EndpointSafetyLevel.High,
                isEnabled = !string.IsNullOrWhiteSpace(coreApiKey),
                priority = 0,
                weight = 1
            };

            return new ModelServiceClient(
                () =>
                {
                    if (msSettings != null && msSettings.enableService && msSettings.endpoints != null)
                    {
                        IReadOnlyList<ModelEndpointConfig> source = envOverlay == null
                            ? (IReadOnlyList<ModelEndpointConfig>)msSettings.endpoints
                            : envOverlay.Apply(msSettings.endpoints);

                        var deepSeekNodes = new List<ModelEndpointConfig>();
                        for (int i = 0; i < source.Count; i++)
                        {
                            var ep = source[i];
                            if (ep != null &&
                                ep.isEnabled &&
                                string.Equals(ep.EffectiveProtocolId, ModelProtocolRegistry.DeepSeekProtocolId, StringComparison.OrdinalIgnoreCase))
                            {
                                deepSeekNodes.Add(ep);
                            }
                        }

                        if (deepSeekNodes.Count > 0)
                            return deepSeekNodes;
                    }

                    return new[] { directNode };
                },
                () => msSettings?.balancingStrategy ?? BalancingStrategy.PriorityFailover);
        }

        private static bool IsDeepSeekOrLoopbackEndpoint(string endpoint)
        {
            return endpoint.IndexOf("deepseek", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   LocalLoopbackValidator.IsLoopbackAddress(endpoint);
        }
    }
}
