using System;
using System.Collections.Generic;

namespace Verse
{
    public interface IExposable
    {
        void ExposeData();
    }

    public class ModSettings
    {
        public virtual void ExposeData() { }
    }

    public enum LookMode
    {
        Undefined,
        Value,
        Deep,
        Reference
    }

    public enum LoadSaveMode
    {
        Inactive,
        Saving,
        LoadingVars,
        ResolvingCrossRefs,
        PostLoadInit
    }

    public static class Scribe
    {
        [ThreadStatic]
        private static LoadSaveMode _mode;

        public static LoadSaveMode mode
        {
            get => _mode;
            set => _mode = value;
        }
    }

    public static class Scribe_Values
    {
        [ThreadStatic]
        private static Dictionary<string, object?>? _activeBuffer;

        public static void BeginSave(Dictionary<string, object?> buffer)
        {
            _activeBuffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            Scribe.mode = LoadSaveMode.Saving;
        }

        public static void BeginLoad(Dictionary<string, object?> buffer, LoadSaveMode loadMode = LoadSaveMode.LoadingVars)
        {
            _activeBuffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            Scribe.mode = loadMode;
        }

        public static void End()
        {
            _activeBuffer = null;
            Scribe.mode = LoadSaveMode.Inactive;
        }

        public static void Look<T>(ref T value, string label, T defaultValue = default!, bool forceSave = false)
        {
            if (_activeBuffer == null || Scribe.mode == LoadSaveMode.Inactive)
                return;

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                _activeBuffer[label] = value;
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (_activeBuffer.TryGetValue(label, out var raw) && raw is T typed)
                {
                    value = typed;
                }
                else
                {
                    value = defaultValue;
                }
            }
        }
    }

    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T> list, string label, LookMode lookMode = LookMode.Deep, params object[] ctorArgs)
        {
            // Stub for test execution
        }
    }

    public static class Translator
    {
        public static bool CanTranslate(this string key) => true;
        public static string Translate(this string key) => key;
    }
}

namespace RimMind.ModelService
{
    public static class RimMindModelServiceMod
    {
        public static Settings.ModelServiceSettings? Settings { get; set; }
    }
}
