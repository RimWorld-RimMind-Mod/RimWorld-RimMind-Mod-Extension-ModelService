using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;

namespace RimMind.ModelService.Client
{
    /// <summary>
    /// Applies automation credentials from environment variables (e.g. RIMMIND_TEST_* / RIMMIND_BACKUP_* /
    /// RIMMIND_DEEPSEEK_*) to key-less matching endpoints WITHOUT mutating persisted <see cref="ModelEndpointConfig"/>
    /// objects. Shadow copies are cached per endpoint id so runtime health/circuit-breaker state survives across requests.
    /// </summary>
    internal sealed class EnvironmentEndpointOverlay
    {
        internal sealed class ProtocolEnvOverride
        {
            public ProtocolEnvOverride(string apiKey, string? endpoint, string? model)
            {
                ApiKey = apiKey;
                Endpoint = endpoint;
                Model = model;
            }

            public string ApiKey { get; }
            public string? Endpoint { get; }
            public string? Model { get; }
        }

        private readonly Dictionary<string, ProtocolEnvOverride> _overridesByProtocol;
        private readonly ConcurrentDictionary<string, ModelEndpointConfig> _shadows =
            new ConcurrentDictionary<string, ModelEndpointConfig>(StringComparer.Ordinal);

        private EnvironmentEndpointOverlay(Dictionary<string, ProtocolEnvOverride> overridesByProtocol)
        {
            _overridesByProtocol = overridesByProtocol;
        }

        /// <summary>Returns null when no automation key is present in the environment.</summary>
        public static EnvironmentEndpointOverlay? FromEnvironment(Func<string, string?>? getEnv = null)
        {
            getEnv ??= Environment.GetEnvironmentVariable;
            var overrides = new Dictionary<string, ProtocolEnvOverride>(StringComparer.OrdinalIgnoreCase);

            var protocols = ModelProtocolRegistry.All;
            for (int i = 0; i < protocols.Count; i++)
            {
                var protocol = protocols[i];
                var binding = protocol.EnvOverrideBinding;
                if (binding == null || binding.ApiKeyVars.Length == 0)
                    continue;

                string? key = FirstNonWhiteSpace(getEnv, binding.ApiKeyVars);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                string? endpoint = FirstNonNull(getEnv, binding.EndpointVars);
                string? model = FirstNonNull(getEnv, binding.ModelVars);
                overrides[protocol.ProtocolId] = new ProtocolEnvOverride(key!, endpoint, model);
            }

            return overrides.Count > 0 ? new EnvironmentEndpointOverlay(overrides) : null;
        }

        /// <summary>
        /// Returns a new list in which key-less endpoints matching a configured protocol override are replaced
        /// by cached shadow copies carrying environment credentials. Source elements are never modified.
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

                if (!string.IsNullOrEmpty(ep.apiKey) ||
                    !_overridesByProtocol.TryGetValue(ep.EffectiveProtocolId, out var protocolOverride))
                {
                    result.Add(ep);
                    continue;
                }

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
                shadow.apiKey = protocolOverride.ApiKey;
                shadow.endpoint = string.IsNullOrEmpty(protocolOverride.Endpoint) ? ep.endpoint : protocolOverride.Endpoint!;
                shadow.modelName = string.IsNullOrEmpty(protocolOverride.Model) ? ep.modelName : protocolOverride.Model!;
                result.Add(shadow);
            }

            return result;
        }

        private static string? FirstNonWhiteSpace(Func<string, string?> getEnv, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                string? val = getEnv(names[i]);
                if (!string.IsNullOrWhiteSpace(val))
                    return val;
            }
            return null;
        }

        private static string? FirstNonNull(Func<string, string?> getEnv, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                string? val = getEnv(names[i]);
                if (val != null)
                    return val;
            }
            return null;
        }
    }
}
