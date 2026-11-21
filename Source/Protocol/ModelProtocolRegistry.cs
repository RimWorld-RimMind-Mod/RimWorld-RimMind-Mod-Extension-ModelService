using System;
using System.Collections.Generic;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Protocol
{
    /// <summary>
    /// Thread-safe registry of <see cref="IModelProtocol"/> implementations and legacy <see cref="ProviderType"/> aliases.
    /// Concrete protocols live in dedicated classes under <c>Source/Protocol/Protocols/</c>.
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

        private static readonly Dictionary<string, string> Aliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(ProviderType.OpenAICompatible)] = OpenAIProtocolId,
                [nameof(ProviderType.AnthropicClaude)] = AnthropicProtocolId,
                [nameof(ProviderType.LocalSubscriptionGateway)] = LocalGatewayProtocolId,
                [nameof(ProviderType.OpenCodeGo)] = OpenCodeGoProtocolId,
                [nameof(ProviderType.DeepSeekOfficial)] = DeepSeekProtocolId,
                ["deepseek_native"] = DeepSeekProtocolId
            };

        private static readonly Dictionary<ProviderType, string> ProviderTypeToProtocolId =
            new Dictionary<ProviderType, string>
            {
                [ProviderType.OpenAICompatible] = OpenAIProtocolId,
                [ProviderType.AnthropicClaude] = AnthropicProtocolId,
                [ProviderType.LocalSubscriptionGateway] = LocalGatewayProtocolId,
                [ProviderType.OpenCodeGo] = OpenCodeGoProtocolId,
                [ProviderType.DeepSeekOfficial] = DeepSeekProtocolId
            };

        private static readonly Dictionary<string, ProviderType> BuiltInProtocolToProviderType =
            new Dictionary<string, ProviderType>(StringComparer.OrdinalIgnoreCase)
            {
                [OpenAIProtocolId] = ProviderType.OpenAICompatible,
                [AnthropicProtocolId] = ProviderType.AnthropicClaude,
                [LocalGatewayProtocolId] = ProviderType.LocalSubscriptionGateway,
                [OpenCodeGoProtocolId] = ProviderType.OpenCodeGo,
                [DeepSeekProtocolId] = ProviderType.DeepSeekOfficial
            };

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

                RegisterCore(OpenAIModelProtocol.Instance);
                RegisterCore(AnthropicModelProtocol.Instance);
                RegisterCore(LocalGatewayModelProtocol.Instance);
                RegisterCore(OpenCodeGoModelProtocol.Instance);
                RegisterCore(DeepSeekModelProtocol.Instance);
            }
        }

        public static void Register(IModelProtocol protocol)
        {
            if (protocol == null) throw new ArgumentNullException(nameof(protocol));
            if (string.IsNullOrWhiteSpace(protocol.ProtocolId))
                throw new ArgumentException("ProtocolId cannot be null or whitespace.", nameof(protocol));

            lock (SyncRoot)
            {
                RegisterCore(protocol);
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
                return true;
            }
        }

        public static bool TryGet(string? protocolId, out IModelProtocol protocol)
        {
            protocol = null!;
            if (string.IsNullOrWhiteSpace(protocolId)) return false;

            string key = NormalizeAlias(protocolId!.Trim());
            lock (SyncRoot)
            {
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

        public static string ToProtocolId(ProviderType providerType) =>
            ProviderTypeToProtocolId.TryGetValue(providerType, out string? id) ? id : OpenAIProtocolId;

        public static bool TryGetBuiltInProviderType(string? protocolId, out ProviderType providerType)
        {
            providerType = ProviderType.OpenAICompatible;
            if (string.IsNullOrWhiteSpace(protocolId))
                return false;

            string normalized = NormalizeAlias(protocolId!.Trim());
            return BuiltInProtocolToProviderType.TryGetValue(normalized, out providerType);
        }

        private static string NormalizeAlias(string id) =>
            Aliases.TryGetValue(id, out string? canonical) ? canonical : id;

        private static void RegisterCore(IModelProtocol protocol)
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
        }
    }
}
