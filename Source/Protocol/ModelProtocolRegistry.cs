using System;
using System.Collections.Generic;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    /// <summary>
    /// Thread-safe registry of pluggable <see cref="IModelProtocol"/> implementations.
    /// Legacy aliases and <see cref="ProviderType"/> mappings are indexed dynamically from each
    /// registered protocol's metadata rather than hardcoded parallel dictionaries.
    /// </summary>
    public static class ModelProtocolRegistry
    {
        public const string OpenAIProtocolId = "openai";
        public const string AnthropicProtocolId = "anthropic";
        public const string LocalGatewayProtocolId = "local_gateway";
        public const string OpenCodeGoProtocolId = "opencode_go";
        public const string DeepSeekProtocolId = "deepseek";

        private static readonly object SyncRoot = new object();
        private static readonly List<IModelProtocol> OrderedProtocols = new List<IModelProtocol>();
        private static readonly Dictionary<string, IModelProtocol> ProtocolsById =
            new Dictionary<string, IModelProtocol>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> AliasesByKey =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<ProviderType, string> ProviderTypeToProtocolId =
            new Dictionary<ProviderType, string>();
        private static readonly Dictionary<string, ProviderType> BuiltInProtocolToProviderType =
            new Dictionary<string, ProviderType>(StringComparer.OrdinalIgnoreCase);

        static ModelProtocolRegistry()
        {
            ResetToDefaults();
        }

        public static IReadOnlyList<IModelProtocol> All
        {
            get
            {
                lock (SyncRoot)
                {
                    return OrderedProtocols.ToArray();
                }
            }
        }

        public static void ResetToDefaults()
        {
            lock (SyncRoot)
            {
                OrderedProtocols.Clear();
                ProtocolsById.Clear();
                AliasesByKey.Clear();
                ProviderTypeToProtocolId.Clear();
                BuiltInProtocolToProviderType.Clear();

                RegisterCore(OpenAIModelProtocol.Instance, isBuiltIn: true);
                RegisterCore(AnthropicModelProtocol.Instance, isBuiltIn: true);
                RegisterCore(LocalGatewayModelProtocol.Instance, isBuiltIn: true);
                RegisterCore(OpenCodeGoModelProtocol.Instance, isBuiltIn: true);
                RegisterCore(DeepSeekModelProtocol.Instance, isBuiltIn: true);
            }
        }

        public static void Register(IModelProtocol protocol)
        {
            if (protocol == null) throw new ArgumentNullException(nameof(protocol));
            if (string.IsNullOrWhiteSpace(protocol.ProtocolId))
                throw new ArgumentException("ProtocolId cannot be null or whitespace.", nameof(protocol));

            lock (SyncRoot)
            {
                RegisterCore(protocol, isBuiltIn: false);
            }
        }

        public static bool Unregister(string protocolId)
        {
            if (string.IsNullOrWhiteSpace(protocolId)) return false;

            string key = protocolId.Trim();
            lock (SyncRoot)
            {
                if (!ProtocolsById.TryGetValue(key, out var existing))
                    return false;

                ProtocolsById.Remove(key);
                OrderedProtocols.RemoveAll(p => string.Equals(p.ProtocolId, existing.ProtocolId, StringComparison.OrdinalIgnoreCase));

                var aliasesToRemove = new List<string>();
                foreach (var kvp in AliasesByKey)
                {
                    if (string.Equals(kvp.Value, existing.ProtocolId, StringComparison.OrdinalIgnoreCase))
                        aliasesToRemove.Add(kvp.Key);
                }
                for (int i = 0; i < aliasesToRemove.Count; i++)
                {
                    AliasesByKey.Remove(aliasesToRemove[i]);
                }

                if (BuiltInProtocolToProviderType.TryGetValue(existing.ProtocolId, out var legacyType))
                {
                    BuiltInProtocolToProviderType.Remove(existing.ProtocolId);
                    if (ProviderTypeToProtocolId.TryGetValue(legacyType, out string? mappedId) &&
                        string.Equals(mappedId, existing.ProtocolId, StringComparison.OrdinalIgnoreCase))
                    {
                        ProviderTypeToProtocolId.Remove(legacyType);
                    }
                }

                return true;
            }
        }

        public static string NormalizeAlias(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return string.Empty;
            string trimmed = id.Trim();
            lock (SyncRoot)
            {
                return AliasesByKey.TryGetValue(trimmed, out string? canonical) ? canonical : trimmed;
            }
        }

        public static bool TryGet(string? protocolId, out IModelProtocol protocol)
        {
            protocol = null!;
            if (string.IsNullOrWhiteSpace(protocolId)) return false;

            lock (SyncRoot)
            {
                string trimmed = protocolId!.Trim();
                string key = AliasesByKey.TryGetValue(trimmed, out string? canonical) ? canonical : trimmed;
                return ProtocolsById.TryGetValue(key, out protocol!);
            }
        }

        public static IModelProtocol? FindById(string? protocolId) =>
            TryGet(protocolId, out var protocol) ? protocol : null;

        public static IModelProtocol Resolve(ModelEndpointConfig? endpoint)
        {
            if (endpoint == null)
                return OpenAIModelProtocol.Instance;

            return Resolve(endpoint.EffectiveProtocolId, endpoint.providerType);
        }

        public static IModelProtocol Resolve(string? protocolId, ProviderType fallbackProviderType = ProviderType.OpenAICompatible)
        {
            if (TryGet(protocolId, out var protocol))
                return protocol;

            string fallbackId = ToProtocolId(fallbackProviderType);
            if (TryGet(fallbackId, out var fallbackProtocol))
                return fallbackProtocol;

            return OpenAIModelProtocol.Instance;
        }

        public static string ToProtocolId(ProviderType providerType)
        {
            lock (SyncRoot)
            {
                return ProviderTypeToProtocolId.TryGetValue(providerType, out string? id)
                    ? id
                    : OpenAIProtocolId;
            }
        }

        public static bool TryGetBuiltInProviderType(string? protocolId, out ProviderType providerType)
        {
            providerType = ProviderType.OpenAICompatible;
            if (string.IsNullOrWhiteSpace(protocolId))
                return false;

            lock (SyncRoot)
            {
                string trimmed = protocolId!.Trim();
                string normalized = AliasesByKey.TryGetValue(trimmed, out string? canonical) ? canonical : trimmed;
                return BuiltInProtocolToProviderType.TryGetValue(normalized, out providerType);
            }
        }

        private static void RegisterCore(IModelProtocol protocol, bool isBuiltIn)
        {
            string key = protocol.ProtocolId.Trim();
            int existingIndex = OrderedProtocols.FindIndex(p =>
                string.Equals(p.ProtocolId, key, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                OrderedProtocols[existingIndex] = protocol;
            }
            else
            {
                OrderedProtocols.Add(protocol);
            }
            ProtocolsById[key] = protocol;

            if (protocol.LegacyAliases != null)
            {
                for (int i = 0; i < protocol.LegacyAliases.Count; i++)
                {
                    string alias = protocol.LegacyAliases[i];
                    if (string.IsNullOrWhiteSpace(alias))
                        continue;

                    string trimmedAlias = alias.Trim();
                    if (isBuiltIn
                        || !AliasesByKey.TryGetValue(trimmedAlias, out string? existingOwner)
                        || !BuiltInProtocolToProviderType.ContainsKey(existingOwner))
                    {
                        AliasesByKey[trimmedAlias] = key;
                    }
                }
            }

            if (isBuiltIn)
            {
                ProviderTypeToProtocolId[protocol.LegacyProviderType] = key;
                BuiltInProtocolToProviderType[key] = protocol.LegacyProviderType;
            }
        }
    }
}
