using System;
using System.Threading;
using RimMind.ModelService.Protocol;
using Verse;

namespace RimMind.ModelService.Models
{
    public class ModelEndpointConfig : IExposable
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "Default Node";

        private ProviderType _providerType = ProviderType.OpenAICompatible;
        private string _protocolId = "";

        public ProviderType providerType
        {
            get => _providerType;
            set
            {
                _providerType = value;
                if (string.IsNullOrWhiteSpace(_protocolId) || ModelProtocolRegistry.TryGetBuiltInProviderType(_protocolId, out _))
                {
                    _protocolId = ModelProtocolRegistry.ToProtocolId(value);
                }
                else if (ModelProtocolRegistry.TryGet(_protocolId, out var customProto) && customProto.LegacyProviderType != value)
                {
                    _protocolId = ModelProtocolRegistry.ToProtocolId(value);
                }
                else if (value != ProviderType.OpenAICompatible)
                {
                    _protocolId = ModelProtocolRegistry.ToProtocolId(value);
                }
            }
        }

        public string protocolId
        {
            get => _protocolId;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _protocolId = string.Empty;
                    return;
                }

                string trimmed = value.Trim();
                if (ModelProtocolRegistry.TryGetBuiltInProviderType(trimmed, out var builtInType))
                {
                    _providerType = builtInType;
                    _protocolId = ModelProtocolRegistry.ToProtocolId(builtInType);
                }
                else
                {
                    _protocolId = trimmed;
                    if (ModelProtocolRegistry.TryGet(trimmed, out var customProto))
                    {
                        _providerType = customProto.LegacyProviderType;
                    }
                }
            }
        }

        public string EffectiveProtocolId
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_protocolId))
                {
                    string trimmed = _protocolId.Trim();
                    if (ModelProtocolRegistry.TryGetBuiltInProviderType(trimmed, out var builtInType))
                    {
                        return builtInType == _providerType
                            ? ModelProtocolRegistry.ToProtocolId(builtInType)
                            : ModelProtocolRegistry.ToProtocolId(_providerType);
                    }
                    return trimmed;
                }
                return ModelProtocolRegistry.ToProtocolId(_providerType);
            }
        }

        public string endpoint = "https://api.openai.com/v1";
        public string apiKey = "";
        public string modelName = "gpt-4o-mini";
        public int priority = 0; // Lower number = higher priority
        public int weight = 1;
        public int timeoutSeconds = 60;
        public bool isEnabled = true;

        public bool IsLoopbackAddress => Security.LocalLoopbackValidator.IsLoopbackAddress(endpoint);

        // Runtime diagnostics (thread-safe, not saved to XML)
        [NonSerialized]
        private int _consecutiveFailures;
        [NonSerialized]
        private long _isolatedUntilMs;
        [NonSerialized]
        private long _lastFailureTimestampMs;
        [NonSerialized]
        private int _lastLatencyMs = -1;
        [NonSerialized]
        private int _isHealthy = 1;

        public long LastFailureTimestampMs
        {
            get => Volatile.Read(ref _lastFailureTimestampMs);
            set => Interlocked.Exchange(ref _lastFailureTimestampMs, value);
        }

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
            LastFailureTimestampMs = 0;
            lastLatencyMs = latencyMs;
            isHealthy = true;
        }

        public void RecordFailure(long currentTick, long cooldownTicks = 60000)
        {
            LastFailureTimestampMs = currentTick;
            int failures = Interlocked.Increment(ref _consecutiveFailures);
            isHealthy = false;
            if (failures >= 3)
            {
                isolatedUntilMs = currentTick + cooldownTicks;
            }
        }

        public void SynchronizeProtocol()
        {
            if (string.IsNullOrWhiteSpace(_protocolId))
            {
                _protocolId = ModelProtocolRegistry.ToProtocolId(_providerType);
                return;
            }

            string trimmed = _protocolId.Trim();
            if (ModelProtocolRegistry.TryGetBuiltInProviderType(trimmed, out var builtInType))
            {
                if (builtInType != _providerType)
                {
                    if (_providerType != ProviderType.OpenAICompatible && builtInType == ProviderType.OpenAICompatible)
                    {
                        _protocolId = ModelProtocolRegistry.ToProtocolId(_providerType);
                    }
                    else
                    {
                        _providerType = builtInType;
                        _protocolId = ModelProtocolRegistry.ToProtocolId(builtInType);
                    }
                }
                else
                {
                    _protocolId = ModelProtocolRegistry.ToProtocolId(builtInType);
                }
                return;
            }

            _protocolId = trimmed;
            if (ModelProtocolRegistry.TryGet(trimmed, out var customProto))
            {
                _providerType = customProto.LegacyProviderType;
            }
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                SynchronizeProtocol();
            }

            Scribe_Values.Look(ref id, "id", Guid.NewGuid().ToString("N"));
            Scribe_Values.Look(ref name, "name", "Default Node");
            Scribe_Values.Look(ref _providerType, "providerType", ProviderType.OpenAICompatible);
            Scribe_Values.Look(ref _protocolId, "protocolId", "");
            Scribe_Values.Look(ref endpoint, "endpoint", "https://api.openai.com/v1");
            Scribe_Values.Look(ref apiKey, "apiKey", "");
            Scribe_Values.Look(ref modelName, "modelName", "gpt-4o-mini");
            Scribe_Values.Look(ref priority, "priority", 0);
            Scribe_Values.Look(ref weight, "weight", 1);
            Scribe_Values.Look(ref timeoutSeconds, "timeoutSeconds", 60);
            Scribe_Values.Look(ref isEnabled, "isEnabled", true);

            SynchronizeProtocol();
        }

        public static ModelEndpointConfig CreateCodexGatewayPreset(int priority = 0) =>
            ModelEndpointPresets.CreateCodexGatewayPreset(priority);

        public static ModelEndpointConfig CreateOpenCodeGoPreset(int priority = 1) =>
            ModelEndpointPresets.CreateOpenCodeGoPreset(priority);
    }
}
