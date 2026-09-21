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
                // fall back to the first enabled one to allow self-healing recovery retry
                return endpoints.FirstOrDefault(e => e.isEnabled);
            }

            if (strategy == BalancingStrategy.PriorityFailover)
            {
                // Lowest priority number = highest preference
                return eligible.OrderBy(e => e.priority).FirstOrDefault();
            }
            else // RoundRobin
            {
                int index = (int)((uint)Interlocked.Increment(ref _roundRobinIndex) % (uint)eligible.Count);
                return eligible[index];
            }
        }
    }
}
