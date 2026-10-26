using System;
using System.Collections.Generic;
using RimMind.ModelService.Models;
using Verse;

namespace RimMind.ModelService.Settings
{
    public class ModelServiceSettings : ModSettings
    {
        public bool enableService = true;
        public BalancingStrategy balancingStrategy = BalancingStrategy.PriorityFailover;
        public List<ModelEndpointConfig> endpoints = new List<ModelEndpointConfig>();

        public object EndpointSyncRoot => endpoints ??= new List<ModelEndpointConfig>();

        public ModelServiceSettings()
        {
            EnsureDefaultEndpoints();
        }

        public ModelEndpointConfig[] GetEndpointsSnapshot()
        {
            lock (EndpointSyncRoot)
            {
                return endpoints.Count == 0
                    ? Array.Empty<ModelEndpointConfig>()
                    : endpoints.ToArray();
            }
        }

        public void EnsureDefaultEndpoints()
        {
            lock (EndpointSyncRoot)
            {
                if (endpoints.Count == 0)
                {
                    endpoints.AddRange(new[]
                    {
                        ModelEndpointPresets.CreateCodexGatewayPreset(0),
                        ModelEndpointPresets.CreateOpenCodeGoPreset(1)
                    });
                }
            }
        }

        public void ResetToDefault()
        {
            lock (EndpointSyncRoot)
            {
                enableService = true;
                balancingStrategy = BalancingStrategy.PriorityFailover;
                endpoints.Clear();
                endpoints.AddRange(new[]
                {
                    ModelEndpointPresets.CreateCodexGatewayPreset(0),
                    ModelEndpointPresets.CreateOpenCodeGoPreset(1)
                });
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enableService, "enableService", true);
            Scribe_Values.Look(ref balancingStrategy, "balancingStrategy", BalancingStrategy.PriorityFailover);

            var existingEndpoints = endpoints;
            Scribe_Collections.Look(ref endpoints, "endpoints", LookMode.Deep);
            if (existingEndpoints != null && !ReferenceEquals(existingEndpoints, endpoints))
            {
                lock (existingEndpoints)
                {
                    existingEndpoints.Clear();
                    if (endpoints != null)
                    {
                        existingEndpoints.AddRange(endpoints);
                    }
                    endpoints = existingEndpoints;
                }
            }

            EnsureDefaultEndpoints();
        }
    }
}
