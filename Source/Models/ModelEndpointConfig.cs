using System;
using System.Threading;
using Verse;

namespace RimMind.ModelService.Models
{
    public class ModelEndpointConfig : IExposable
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "Default Node";
        public ProviderType providerType = ProviderType.OpenAICompatible;
        public string endpoint = "https://api.openai.com/v1";
        public string apiKey = "";
        public string modelName = "gpt-4o-mini";
        public int priority = 0; // Lower number = higher priority
        public int weight = 1;
        public bool isEnabled = true;

        // Runtime diagnostics (thread-safe, not saved to XML)
        private int _consecutiveFailures;
        private long _isolatedUntilMs;
        private int _lastLatencyMs = -1;
        private int _isHealthy = 1;

        public int consecutiveFailures
        {
            get => Volatile.Read(ref _consecutiveFailures);
            set => Interlocked.Exchange(ref _consecutiveFailures, value);
        }

        public long isolatedUntilMs
        {
            get => Volatile.Read(ref _isolatedUntilMs);
            set => Interlocked.Exchange(ref _isolatedUntilMs, value);
        }

        public int lastLatencyMs
        {
            get => Volatile.Read(ref _lastLatencyMs);
            set => Interlocked.Exchange(ref _lastLatencyMs, value);
        }

        public bool isHealthy
        {
            get => Volatile.Read(ref _isHealthy) == 1;
            set => Interlocked.Exchange(ref _isHealthy, value ? 1 : 0);
        }

        public bool IsTemporarilyIsolated(long currentTick) =>
            consecutiveFailures >= 3 && currentTick < isolatedUntilMs;

        public void RecordSuccess(int latencyMs)
        {
            consecutiveFailures = 0;
            isolatedUntilMs = 0;
            lastLatencyMs = latencyMs;
            isHealthy = true;
        }

        public void RecordFailure(long currentTick, long cooldownTicks = 60000)
        {
            int failures = Interlocked.Increment(ref _consecutiveFailures);
            isHealthy = false;
            if (failures >= 3)
            {
                isolatedUntilMs = currentTick + cooldownTicks;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", Guid.NewGuid().ToString("N"));
            Scribe_Values.Look(ref name, "name", "Default Node");
            Scribe_Values.Look(ref providerType, "providerType", ProviderType.OpenAICompatible);
            Scribe_Values.Look(ref endpoint, "endpoint", "https://api.openai.com/v1");
            Scribe_Values.Look(ref apiKey, "apiKey", "");
            Scribe_Values.Look(ref modelName, "modelName", "gpt-4o-mini");
            Scribe_Values.Look(ref priority, "priority", 0);
            Scribe_Values.Look(ref weight, "weight", 1);
            Scribe_Values.Look(ref isEnabled, "isEnabled", true);
        }

        public static ModelEndpointConfig CreateCodexGatewayPreset(int priority = 0) =>
            ModelEndpointPresets.CreateCodexGatewayPreset(priority);

        public static ModelEndpointConfig CreateOpenCodeGoPreset(int priority = 1) =>
            ModelEndpointPresets.CreateOpenCodeGoPreset(priority);
    }
}
