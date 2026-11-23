using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using RimMind.Application.Features.Llm;
using RimMind.ModelService.Models;

namespace RimMind.ModelService.Balancing
{
    public interface ILoadBalancer
    {
        IReadOnlyList<ModelEndpointConfig> GetEligibleEndpoints(
            IReadOnlyList<ModelEndpointConfig>? endpoints,
            long currentMs);

        ModelEndpointConfig? SelectEndpoint(
            IReadOnlyList<ModelEndpointConfig>? endpoints,
            BalancingStrategy strategy,
            long currentMs);
    }

    public class LoadBalancer : ILoadBalancer
    {
        private readonly EndpointFailoverPolicy _failoverPolicy;
        private readonly ConcurrentDictionary<string, int> _observedSuccessGeneration =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int _roundRobinIndex = 0;

        public LoadBalancer()
            : this(null)
        {
        }

        public LoadBalancer(EndpointFailoverPolicy? failoverPolicy)
        {
            _failoverPolicy = failoverPolicy ?? new EndpointFailoverPolicy(
                defaultIsolationMs: ModelEndpointConfig.DefaultCooldownMs,
                transientRateLimitIsolationMs: EndpointFailoverPolicy.DefaultTransientRateLimitIsolationMs,
                permanentIsolationMs: EndpointFailoverPolicy.DefaultPermanentIsolationMs);
        }

        public EndpointFailoverPolicy FailoverPolicy => _failoverPolicy;

        internal static string BuildNodeKey(ModelEndpointConfig endpoint)
        {
            if (endpoint == null) return string.Empty;
            string endpointPart = (endpoint.endpoint ?? string.Empty).Trim().TrimEnd('/');
            string modelPart = (endpoint.modelName ?? string.Empty).Trim();
            string keyFingerprint = ComputeApiKeyFingerprint(endpoint.apiKey);
            return $"{endpoint.id ?? string.Empty}|{endpointPart}|{modelPart}|{keyFingerprint}";
        }

        private static string ComputeApiKeyFingerprint(string? apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return string.Empty;

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(apiKey!));
                return $"{hash[0]:x2}{hash[1]:x2}{hash[2]:x2}{hash[3]:x2}";
            }
        }

        internal void ObserveNodeRecoveryBaseline(ModelEndpointConfig endpoint)
        {
            if (endpoint == null) return;
            _observedSuccessGeneration[BuildNodeKey(endpoint)] = endpoint.SuccessGeneration;
        }

        private bool IsNodeIsolated(ModelEndpointConfig endpoint, long currentMs)
        {
            string key = BuildNodeKey(endpoint);
            SyncNodeRecovery(endpoint, key);
            return endpoint.IsTemporarilyIsolated(currentMs) || _failoverPolicy.IsIsolated(key, currentMs);
        }

        private void SyncNodeRecovery(ModelEndpointConfig endpoint, string key)
        {
            int gen = endpoint.SuccessGeneration;
            if (_observedSuccessGeneration.TryGetValue(key, out int prevGen))
            {
                if (gen != prevGen)
                {
                    _observedSuccessGeneration[key] = gen;
                    _failoverPolicy.RecordSuccess(key);
                }
            }
            else
            {
                _observedSuccessGeneration[key] = gen;
            }
        }

        public IReadOnlyList<ModelEndpointConfig> GetEligibleEndpoints(
            IReadOnlyList<ModelEndpointConfig>? endpoints,
            long currentMs)
        {
            if (endpoints == null || endpoints.Count == 0)
                return Array.Empty<ModelEndpointConfig>();

            var eligible = new List<ModelEndpointConfig>(endpoints.Count);
            for (int i = 0; i < endpoints.Count; i++)
            {
                var ep = endpoints[i];
                if (ep != null && ep.isEnabled && !IsNodeIsolated(ep, currentMs))
                {
                    eligible.Add(ep);
                }
            }
            return eligible;
        }

        public ModelEndpointConfig? SelectEndpoint(
            IReadOnlyList<ModelEndpointConfig>? endpoints,
            BalancingStrategy strategy,
            long currentMs)
        {
            if (endpoints == null || endpoints.Count == 0)
                return null;

            var eligible = GetEligibleEndpoints(endpoints, currentMs);
            if (eligible.Count == 0)
            {
                // If all candidate endpoints are temporarily isolated due to circuit breaker,
                // prefer non-exhausted nodes via Core's EndpointFailoverPolicy, then pick the
                // enabled one whose isolation window / last failure is oldest for half-open recovery.
                var fallbackCandidates = endpoints
                    .Where(e => e != null && e.isEnabled)
                    .OrderBy(e => e.isolatedUntilMs)
                    .ThenBy(e => e.LastFailureTimestampMs)
                    .ThenBy(e => e.priority)
                    .ToList();

                if (fallbackCandidates.Count == 0)
                    return null;

                var orderedFallback = _failoverPolicy.OrderCandidates(fallbackCandidates, BuildNodeKey, currentMs);
                return orderedFallback.Count > 0 ? orderedFallback[0] : fallbackCandidates[0];
            }

            if (strategy == BalancingStrategy.PriorityFailover)
            {
                // Lowest priority number = highest preference, ordered through EndpointFailoverPolicy.
                var byPriority = eligible.OrderBy(e => e.priority).ToList();
                var ordered = _failoverPolicy.OrderCandidates(byPriority, BuildNodeKey, currentMs);
                return ordered.Count > 0 ? ordered[0] : byPriority[0];
            }
            else // RoundRobin (weighted by Math.Max(1, ep.weight))
            {
                long totalWeight = 0;
                for (int i = 0; i < eligible.Count; i++)
                {
                    totalWeight += Math.Max(1, eligible[i].weight);
                }

                ulong tick = (ulong)(uint)Interlocked.Increment(ref _roundRobinIndex);
                long target = (long)(tick % (ulong)totalWeight);
                long cumulative = 0;
                for (int i = 0; i < eligible.Count; i++)
                {
                    cumulative += Math.Max(1, eligible[i].weight);
                    if (target < cumulative)
                    {
                        return eligible[i];
                    }
                }
                return eligible[0];
            }
        }
    }
}
