using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;

namespace RimMind.ModelService.Client
{
    /// <summary>
    /// Applies automation credentials from environment variables (RIMMIND_TEST_* / RIMMIND_BACKUP_*) to
    /// OpenCodeGo endpoints that have no API key, WITHOUT mutating the persisted <see cref="ModelEndpointConfig"/>
    /// objects. Previously the factory wrote these values into the live ModSettings, so the next settings save
    /// stored the secret in plain text and overwrote the player's endpoint URL and model.
    /// Shadow copies are cached per endpoint id so runtime health/circuit-breaker state survives across requests.
    /// </summary>
    internal sealed class EnvironmentEndpointOverlay
    {
        private readonly string? _apiKey;
        private readonly string? _endpoint;
        private readonly string? _model;
        private readonly string? _deepSeekApiKey;
        private readonly string? _deepSeekEndpoint;
        private readonly string? _deepSeekModel;
        private readonly ConcurrentDictionary<string, ModelEndpointConfig> _shadows =
            new ConcurrentDictionary<string, ModelEndpointConfig>(StringComparer.Ordinal);

        private EnvironmentEndpointOverlay(
            string? apiKey,
            string? endpoint,
            string? model,
            string? deepSeekApiKey,
            string? deepSeekEndpoint,
            string? deepSeekModel)
        {
            _apiKey = apiKey;
            _endpoint = endpoint;
            _model = model;
            _deepSeekApiKey = deepSeekApiKey;
            _deepSeekEndpoint = deepSeekEndpoint;
            _deepSeekModel = deepSeekModel;
        }

        /// <summary>Returns null when no automation key is present in the environment.</summary>
        public static EnvironmentEndpointOverlay? FromEnvironment(Func<string, string?>? getEnv = null)
        {
            getEnv ??= Environment.GetEnvironmentVariable;
            string? testKey = getEnv("RIMMIND_TEST_API_KEY");
            string? backupKey = getEnv("RIMMIND_BACKUP_API_KEY");
            string? key = !string.IsNullOrWhiteSpace(backupKey) ? backupKey : testKey;

            string? deepSeekKey = getEnv("RIMMIND_DEEPSEEK_API_KEY");
            if (string.IsNullOrWhiteSpace(deepSeekKey))
                deepSeekKey = getEnv("DEEPSEEK_API_KEY");

            if (string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(deepSeekKey))
                return null;

            string? endpoint = getEnv("RIMMIND_BACKUP_ENDPOINT") ?? getEnv("RIMMIND_TEST_ENDPOINT");
            string? model = getEnv("RIMMIND_BACKUP_MODEL") ?? getEnv("RIMMIND_TEST_MODEL");
            string? deepSeekEndpoint = getEnv("RIMMIND_DEEPSEEK_ENDPOINT");
            string? deepSeekModel = getEnv("RIMMIND_DEEPSEEK_MODEL");
            return new EnvironmentEndpointOverlay(
                string.IsNullOrWhiteSpace(key) ? null : key,
                endpoint,
                model,
                string.IsNullOrWhiteSpace(deepSeekKey) ? null : deepSeekKey,
                deepSeekEndpoint,
                deepSeekModel);
        }

        /// <summary>
        /// Returns a new list in which key-less OpenCodeGo or DeepSeek endpoints are replaced by cached shadow
        /// copies carrying environment credentials. The source list and its elements are never modified.
        /// </summary>
        public IReadOnlyList<ModelEndpointConfig> Apply(IReadOnlyList<ModelEndpointConfig> source)
        {
            if (source == null || source.Count == 0)
                return source ?? Array.Empty<ModelEndpointConfig>();

            ModelEndpointConfig[] snapshot;
            lock (source)
            {
                snapshot = new ModelEndpointConfig[source.Count];
                for (int i = 0; i < source.Count; i++)
                    snapshot[i] = source[i];
            }

            var result = new List<ModelEndpointConfig>(snapshot.Length);
            foreach (var ep in snapshot)
            {
                if (ep == null)
                    continue;

                if (!string.IsNullOrEmpty(ep.apiKey))
                {
                    result.Add(ep);
                    continue;
                }

                bool isOpenCodeMatch =
                    string.Equals(ep.EffectiveProtocolId, ModelProtocolRegistry.OpenCodeGoProtocolId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(_apiKey);
                bool isDeepSeekMatch =
                    string.Equals(ep.EffectiveProtocolId, ModelProtocolRegistry.DeepSeekProtocolId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(_deepSeekApiKey);

                if (!isOpenCodeMatch && !isDeepSeekMatch)
                {
                    result.Add(ep);
                    continue;
                }

                string overlayKey = isDeepSeekMatch ? _deepSeekApiKey! : _apiKey!;
                string? overlayEndpoint = isDeepSeekMatch ? _deepSeekEndpoint : _endpoint;
                string? overlayModel = isDeepSeekMatch ? _deepSeekModel : _model;

                var shadow = _shadows.GetOrAdd(ep.id ?? string.Empty, _ => new ModelEndpointConfig());
                shadow.id = ep.id ?? string.Empty;
                shadow.name = ep.name;
                shadow.providerType = ep.providerType;
                shadow.protocolId = ep.EffectiveProtocolId;
                shadow.priority = ep.priority;
                shadow.weight = ep.weight;
                shadow.timeoutSeconds = ep.timeoutSeconds;
                shadow.isEnabled = ep.isEnabled;
                shadow.thinkingPolicy = ep.thinkingPolicy;
                shadow.safetyLevel = ep.safetyLevel;
                shadow.apiKey = overlayKey;
                shadow.endpoint = string.IsNullOrEmpty(overlayEndpoint) ? ep.endpoint : overlayEndpoint!;
                shadow.modelName = string.IsNullOrEmpty(overlayModel) ? ep.modelName : overlayModel!;
                result.Add(shadow);
            }

            return result;
        }
    }
}
