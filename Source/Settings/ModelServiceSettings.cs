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

        public ModelServiceSettings()
        {
            EnsureDefaultEndpoints();
        }

        public void EnsureDefaultEndpoints()
        {
            if (endpoints == null)
            {
                endpoints = new List<ModelEndpointConfig>();
            }

            if (endpoints.Count == 0)
            {
                endpoints.Add(ModelEndpointPresets.CreateCodexGatewayPreset(0));
                endpoints.Add(ModelEndpointPresets.CreateOpenCodeGoPreset(1));
            }
        }

        public void ResetToDefault()
        {
            enableService = true;
            balancingStrategy = BalancingStrategy.PriorityFailover;
            endpoints = new List<ModelEndpointConfig>();
            EnsureDefaultEndpoints();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enableService, "enableService", true);
            Scribe_Values.Look(ref balancingStrategy, "balancingStrategy", BalancingStrategy.PriorityFailover);
            Scribe_Collections.Look(ref endpoints, "endpoints", LookMode.Deep);

            EnsureDefaultEndpoints();
        }
    }
}
