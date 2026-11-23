using System;
using System.Collections.Generic;
using System.Net.Http;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    public sealed class OpenCodeGoModelProtocol : OpenAIModelProtocol
    {
        public static new readonly OpenCodeGoModelProtocol Instance = new OpenCodeGoModelProtocol();

        private static readonly string[] Aliases = { nameof(ProviderType.OpenCodeGo) };

        private static readonly ProtocolCapabilities ProtocolCaps = new ProtocolCapabilities(
            supportsNativeThinking: false,
            supportsSafetyCheck: false,
            supportsJsonSchema: true,
            requiresReasoningContentInToolHistory: false,
            requiresLoopback: false,
            statusBadgeLabelKey: "RimMind.ModelService.Security.OpenCodeGoSessionRouted",
            statusBadgeFallback: "OpenCode Go Direct",
            statusBadgeTooltipKey: "RimMind.ModelService.Security.OpenCodeGoSessionRouted.Desc",
            statusBadgeTooltipFallback: "Automatically mounts x-opencode-session header for native OpenCode Go subscription routing.");

        private static readonly ProtocolEnvBinding EnvBinding = new ProtocolEnvBinding(
            apiKeyVars: new[] { "RIMMIND_BACKUP_API_KEY", "RIMMIND_TEST_API_KEY" },
            endpointVars: new[] { "RIMMIND_BACKUP_ENDPOINT", "RIMMIND_TEST_ENDPOINT" },
            modelVars: new[] { "RIMMIND_BACKUP_MODEL", "RIMMIND_TEST_MODEL" });

        private static readonly ProtocolPresetDescriptor Preset = new ProtocolPresetDescriptor(
            buttonLabelKey: "RimMind.ModelService.Settings.AddOpenCodeGoPreset",
            buttonLabelFallback: "+ OpenCode Go Preset",
            tooltipKey: "RimMind.ModelService.Settings.AddOpenCodeGoPreset.Desc",
            tooltipFallback: "Quickly add OpenCode Go subscription endpoint preset.",
            buttonWidth: 160f,
            createEndpoint: priority => new ModelEndpointConfig
            {
                id = Guid.NewGuid().ToString("N"),
                name = "OpenCode Go (Direct)",
                endpoint = Instance.DefaultEndpointUrl,
                modelName = Instance.DefaultModel,
                protocolId = ModelProtocolRegistry.OpenCodeGoProtocolId,
                providerType = ProviderType.OpenCodeGo,
                isEnabled = true,
                priority = priority,
                weight = 1
            });

        public override string ProtocolId => ModelProtocolRegistry.OpenCodeGoProtocolId;
        public override ProviderType LegacyProviderType => ProviderType.OpenCodeGo;
        public override IReadOnlyList<string> LegacyAliases => Aliases;
        public override string DisplayLabelKey => "RimMind.ModelService.Provider.OpenCodeGo";
        public override string FallbackLabel => "OpenCode Go (Subscription)";
        public override string DefaultEndpointUrl => "https://opencode.ai/zen/go/v1";
        public override string DefaultModel => "deepseek-v4.1-flash";
        public override ProtocolCapabilities Capabilities => ProtocolCaps;
        public override ProtocolPresetDescriptor? PresetDescriptor => Preset;
        public override ProtocolEnvBinding? EnvOverrideBinding => EnvBinding;

        public override void ApplyHeaders(HttpRequestMessage request, ModelEndpointConfig endpoint, string? requestId)
        {
            base.ApplyHeaders(request, endpoint, requestId);
            if (request == null) return;

            ApplyOpenCodeSessionHeader(request, requestId, fallbackGuidLength: 16);
        }

        internal static void ApplyOpenCodeSessionHeader(HttpRequestMessage request, string? requestId, int fallbackGuidLength)
        {
            string sessionId = !string.IsNullOrEmpty(requestId)
                ? requestId!
                : Guid.NewGuid().ToString("N").Substring(0, fallbackGuidLength);
            string headerValue = sessionId.StartsWith("rimmind-", StringComparison.OrdinalIgnoreCase)
                ? sessionId
                : "rimmind-" + sessionId;
            request.Headers.TryAddWithoutValidation("x-opencode-session", headerValue);
        }
    }
}
