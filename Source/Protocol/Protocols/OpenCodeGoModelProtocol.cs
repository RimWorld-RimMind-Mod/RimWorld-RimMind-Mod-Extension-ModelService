using System;
using System.Net.Http;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    public sealed class OpenCodeGoModelProtocol : OpenAIModelProtocol
    {
        public static new readonly OpenCodeGoModelProtocol Instance = new OpenCodeGoModelProtocol();

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

        public override string ProtocolId => ModelProtocolRegistry.OpenCodeGoProtocolId;
        public override ProviderType LegacyProviderType => ProviderType.OpenCodeGo;
        public override string DisplayLabelKey => "RimMind.ModelService.Provider.OpenCodeGo";
        public override string FallbackLabel => "OpenCode Go (Subscription)";
        public override string DefaultEndpointUrl => "https://opencode.ai/zen/go/v1";
        public override string DefaultModel => "deepseek-v4.1-flash";
        public override ProtocolCapabilities Capabilities => ProtocolCaps;

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
