using RimMind.Application.Common.Interfaces.Audio;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.ModelService.Audio;
using RimMind.ModelService.Client;
using RimMind.ModelService.Integration;
using RimMind.ModelService.Settings;
using RimMind.Presentation.Api;
using RimMind.Presentation.Settings;
using UnityEngine;
using Verse;

namespace RimMind.ModelService
{
    public class RimMindModelServiceMod : Mod
    {
        public static RimMindModelServiceMod? Instance { get; private set; }
        public static ModelServiceSettings Settings { get; private set; } = null!;

        public RimMindModelServiceMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<ModelServiceSettings>();
        }

        public override string SettingsCategory() =>
            RimMindText.TrOr("RimMind.ModelService.Settings.Category", "RimMind - Model Service");

        public override void DoSettingsWindowContents(Rect inRect)
        {
            ModelServiceSettingsDrawer.DrawSettings(inRect, Settings);
        }
    }

    [StaticConstructorOnStartup]
    public static class ModelServiceInitializer
    {
        static ModelServiceInitializer()
        {
            try
            {
                ModelServiceClientFactory.RegisterDescriptor();

                var factoryRegistry = RimMindAPI.Ext.Get<IAIClientFactory>();
                if (factoryRegistry != null)
                {
                    factoryRegistry.Register(new DeepSeekClientFactory());
                    factoryRegistry.Register(new OpenCodeGoClientFactory());
                    factoryRegistry.Register(new ModelServiceClientFactory());
                    Log.Message("[RimMind-ModelService] Extended model service providers registered successfully (deepseek_native, opencode, extended_service).");
                }

                var ttsRegistry = RimMindAPI.Ext.Get<ISpeechSynthesizer>();
                if (ttsRegistry != null)
                {
                    ttsRegistry.Register(new DeepSeekSpeechSynthesizer());
                }

                var asrRegistry = RimMindAPI.Ext.Get<ISpeechRecognizer>();
                if (asrRegistry != null)
                {
                    asrRegistry.Register(new DeepSeekSpeechRecognizer());
                }

                var settingsTabRegistry = RimMindAPI.Ext.Get<ISettingsTab>();
                if (settingsTabRegistry != null)
                {
                    settingsTabRegistry.Register(new ModelServiceSettingsTab());
                    Log.Message("[RimMind-ModelService] Model service settings tab registered successfully.");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimMind-ModelService] Failed to register extended model service components: {ex.Message}");
            }
        }
    }
}
