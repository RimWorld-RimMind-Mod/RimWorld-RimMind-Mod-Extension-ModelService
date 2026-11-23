using System;
using RimMind.ModelService.Client;
using RimMind.ModelService.Protocol;

namespace RimMind.ModelService.Audio
{
    internal static class DeepSeekAudioEndpointResolver
    {
        public static (string rootBaseUrl, string apiKey)? Resolve()
        {
            var msSettings = RimMindModelServiceMod.Settings;
            var envOverlay = EnvironmentEndpointOverlay.FromEnvironment();

            var best = EndpointPoolResolver.ResolveBestEndpoint(
                msSettings,
                envOverlay,
                protocolIdFilter: ModelProtocolRegistry.DeepSeekProtocolId,
                requireEnableService: false,
                requireApiKey: true);

            if (best != null)
            {
                return (NormalizeRootBaseUrl(best.endpoint), best.apiKey.Trim());
            }

            var binding = DeepSeekModelProtocol.Instance.EnvOverrideBinding;
            if (binding != null)
            {
                string? envKey = FirstNonWhiteSpaceEnv(binding.ApiKeyVars);
                if (!string.IsNullOrWhiteSpace(envKey))
                {
                    string? envEndpoint = FirstNonWhiteSpaceEnv(binding.EndpointVars);
                    return (NormalizeRootBaseUrl(envEndpoint), envKey!.Trim());
                }
            }

            return null;
        }

        private static string? FirstNonWhiteSpaceEnv(string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                string? val = Environment.GetEnvironmentVariable(names[i]);
                if (!string.IsNullOrWhiteSpace(val))
                    return val;
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
