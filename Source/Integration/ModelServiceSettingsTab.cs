using RimMind.ModelService.Client;
using RimMind.ModelService.Settings;
using RimMind.Presentation.Api;
using RimMind.Presentation.Settings;
using UnityEngine;

namespace RimMind.ModelService.Integration
{
    public sealed class ModelServiceSettingsTab : ISettingsTab
    {
        public string Id => ModelServiceClientFactory.ModelServiceSettingsTabId;
        public string OwnerModId => ModelServiceClientFactory.ModelServiceOwnerModId;
        public string Label => RimMindText.TrOr("RimMind.ModelService.Settings.Category", "Model Service");

        public void Draw(Rect rect)
        {
            if (RimMindModelServiceMod.Settings != null)
            {
                ModelServiceSettingsDrawer.DrawSettings(rect, RimMindModelServiceMod.Settings);
            }
        }
    }
}
