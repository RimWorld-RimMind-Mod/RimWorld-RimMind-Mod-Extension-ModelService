using System;
using System.Collections.Generic;
using RimMind.ModelService.Models;
using RimMind.ModelService.Settings;

namespace RimMind.ModelService.Client
{
    /// <summary>
    /// Thread-safe endpoint pool snapshot and protocol-filtered selection helper shared across
    /// client factories and audio resolvers. Always snapshots through <see cref="ModelServiceSettings.GetEndpointsSnapshot"/>
    /// under <see cref="ModelServiceSettings.EndpointSyncRoot"/> before applying environment overlays.
    /// </summary>
    internal static class EndpointPoolResolver
    {
        public static IReadOnlyList<ModelEndpointConfig> GetEndpoints(
            ModelServiceSettings? settings,
            EnvironmentEndpointOverlay? overlay,
            string? protocolIdFilter = null,
            bool requireEnableService = true,
            bool enabledOnly = false)
        {
            if (settings == null || (requireEnableService && !settings.enableService))
                return Array.Empty<ModelEndpointConfig>();

            var snapshot = settings.GetEndpointsSnapshot();
            if (snapshot.Length == 0)
                return Array.Empty<ModelEndpointConfig>();

            IReadOnlyList<ModelEndpointConfig> candidates = overlay == null
                ? (IReadOnlyList<ModelEndpointConfig>)snapshot
                : overlay.Apply(snapshot);

            if (string.IsNullOrEmpty(protocolIdFilter) && !enabledOnly)
                return candidates;

            var filtered = new List<ModelEndpointConfig>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                var ep = candidates[i];
                if (ep == null)
                    continue;
                if (enabledOnly && !ep.isEnabled)
                    continue;
                if (!string.IsNullOrEmpty(protocolIdFilter) &&
                    !string.Equals(ep.EffectiveProtocolId, protocolIdFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                filtered.Add(ep);
            }

            return filtered;
        }

        public static ModelEndpointConfig? ResolveBestEndpoint(
            ModelServiceSettings? settings,
            EnvironmentEndpointOverlay? overlay,
            string? protocolIdFilter = null,
            bool requireEnableService = false,
            bool requireApiKey = false)
        {
            var candidates = GetEndpoints(
                settings,
                overlay,
                protocolIdFilter: protocolIdFilter,
                requireEnableService: requireEnableService,
                enabledOnly: true);

            ModelEndpointConfig? best = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                var ep = candidates[i];
                if (requireApiKey && string.IsNullOrWhiteSpace(ep.apiKey))
                    continue;

                if (ep.isHealthy)
                    return ep;

                if (best == null)
                    best = ep;
            }

            return best;
        }
    }
}
