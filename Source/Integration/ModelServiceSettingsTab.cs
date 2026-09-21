using RimMind.ModelService.Settings;
using RimMind.Presentation.Settings;
using UnityEngine;
using Verse;

namespace RimMind.ModelService.Integration
{
    public sealed class ModelServiceSettingsTab : ISettingsTab
    {
        public string Id => "model_service";
        public string OwnerModId => "RimMindModelService";
        public string Label => "RimMind.ModelService.Settings.Category".Translate();

        public void Draw(Rect rect)
        {
            if (RimMindModelServiceMod.Settings != null)
            {
                ModelServiceSettingsDrawer.DrawSettings(rect, RimMindModelServiceMod.Settings);
            }
        }
    }
}
