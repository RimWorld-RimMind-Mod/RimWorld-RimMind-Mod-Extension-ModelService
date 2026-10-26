using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        private int _roundRobinIndex = 0;

        public IReadOnlyList<ModelEndpointConfig> GetEligibleEndpoints(
            IReadOnlyList<ModelEndpointConfig>? endpoints,
            long currentMs)
        {
            if (endpoints == null || endpoints.Count == 0)
                return Array.Empty<ModelEndpointConfig>();

            return endpoints
                .Where(e => e.isEnabled && !e.IsTemporarilyIsolated(currentMs))
                .ToList();
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
                // fall back to the enabled one whose isolation window / last failure is oldest
                // to allow self-healing half-open recovery retry.
                return endpoints
                    .Where(e => e.isEnabled)
                    .OrderBy(e => e.isolatedUntilMs)
                    .ThenBy(e => e.LastFailureTimestampMs)
                    .ThenBy(e => e.priority)
                    .FirstOrDefault();
            }

            if (strategy == BalancingStrategy.PriorityFailover)
            {
                // Lowest priority number = highest preference
                return eligible.OrderBy(e => e.priority).FirstOrDefault();
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
