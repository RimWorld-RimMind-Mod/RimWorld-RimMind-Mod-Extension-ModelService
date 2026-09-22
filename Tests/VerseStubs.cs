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

    public static class Scribe_Values
    {
        public static void Look<T>(ref T value, string label, T defaultValue = default!, bool forceSave = false)
        {
            // Stub for test execution
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
        public static string Translate(string key) => key;
    }
}

namespace RimMind.ModelService
{
    public static class RimMindModelServiceMod
    {
        public static Settings.ModelServiceSettings? Settings { get; set; }
    }
}
