using System;
using System.Collections.Generic;
using RimMind.ModelService.Client;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;

namespace RimMind.ModelService.Audio
{
    internal static class DeepSeekAudioEndpointResolver
    {
        public static (string rootBaseUrl, string apiKey)? Resolve()
        {
            var msSettings = RimMindModelServiceMod.Settings;
            var envOverlay = EnvironmentEndpointOverlay.FromEnvironment();

            if (msSettings != null && msSettings.endpoints != null)
            {
                IReadOnlyList<ModelEndpointConfig> candidates = envOverlay == null
                    ? (IReadOnlyList<ModelEndpointConfig>)msSettings.endpoints
                    : envOverlay.Apply(msSettings.endpoints);

                ModelEndpointConfig? best = null;
                for (int i = 0; i < candidates.Count; i++)
                {
                    var ep = candidates[i];
                    if (ep == null || !ep.isEnabled || string.IsNullOrWhiteSpace(ep.apiKey))
                        continue;
                    if (!string.Equals(ep.EffectiveProtocolId, ModelProtocolRegistry.DeepSeekProtocolId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (ep.isHealthy)
                    {
                        best = ep;
                        break;
                    }

                    if (best == null)
                        best = ep;
                }

                if (best != null)
                {
                    return (NormalizeRootBaseUrl(best.endpoint), best.apiKey.Trim());
                }
            }

            string? envKey = Environment.GetEnvironmentVariable("RIMMIND_DEEPSEEK_API_KEY");
            if (string.IsNullOrWhiteSpace(envKey))
            {
                envKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            }

            if (!string.IsNullOrWhiteSpace(envKey))
            {
                string? envEndpoint = Environment.GetEnvironmentVariable("RIMMIND_DEEPSEEK_ENDPOINT");
                return (NormalizeRootBaseUrl(envEndpoint), envKey!.Trim());
            }

            return null;
        }

        public static string NormalizeRootBaseUrl(string? rawEndpoint)
        {
            if (string.IsNullOrWhiteSpace(rawEndpoint))
                return "https://api.deepseek.com";

            string url = rawEndpoint!.Trim().TrimEnd('/');
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring(0, url.Length - "/chat/completions".Length).TrimEnd('/');
            }

            if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring(0, url.Length - "/v1".Length).TrimEnd('/');
            }

            return string.IsNullOrEmpty(url) ? "https://api.deepseek.com" : url;
        }
    }
}
