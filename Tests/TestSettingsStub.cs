using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Client;
using RimMind.Domain.Enums;

namespace RimMind.ModelService.Tests
{
    internal sealed class TestSettingsStub : ISettingsProvider
    {
        private readonly Dictionary<string, string> _providerOptions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string ApiKey { get; set; } = string.Empty;
        public string ApiEndpoint { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string Provider { get; set; } = "opencode";
        public bool ForceJsonMode { get; set; } = true;
        public int MaxTokens { get; set; } = 800;
        public float DefaultTemperature { get; set; } = 0.7f;
        public bool DebugLogging { get; set; }
        public int MaxConcurrentRequests { get; set; } = 2;
        public int MaxRetryCount { get; set; } = 2;
        public int RequestTimeoutMs { get; set; } = 30000;
        public int RequestExpireTicks { get; set; } = 600;
        public int BehaviorHistoryMax { get; set; } = 10;
        public int QueueProcessInterval { get; set; } = 10;
        public int DefaultModCooldownTicks { get; set; } = 600;
        public float ActivityFrequencyScale { get; set; } = 1.0f;
        public bool AutoActivateColonistAgents { get; set; }
        public bool IsConfigured => !string.IsNullOrEmpty(ApiKey);
        public void Persist() { }

        public string GetProviderOption(string key) =>
            !string.IsNullOrEmpty(key) && _providerOptions.TryGetValue(key, out var value)
                ? value
                : string.Empty;

        public void SetProviderOption(string key, string value)
        {
            if (!string.IsNullOrEmpty(key))
            {
                _providerOptions[key] = value ?? string.Empty;
            }
        }

        public int AgentTickInterval => 600;
        public int ThinkCooldownTicks => 300;
        public int MaxToolCallDepth => 3;
        public IReadOnlyList<BackupEndpointNode>? BackupEndpoints => null;
        public int CircuitBreakerFailureThreshold => 5;
        public int CircuitBreakerOpenDurationSec => 60;
        public int ContextCalibrateInterval { get; set; } = 1200;
        public int ContextDiffLifetimeTicks { get; set; } = 600;
        public bool RequestOverlayEnabled { get; set; } = true;
        public float RequestOverlayX { get; set; } = 20f;
        public float RequestOverlayY { get; set; } = 20f;
        public float RequestOverlayW { get; set; } = 300f;
        public float RequestOverlayH { get; set; } = 200f;
        public bool ShowAgentProgressFloat { get; set; }
        public bool RequestOverlayAutoHideWhenEmpty { get; set; } = true;
        public bool EnableFloatingMentalMonitor { get; set; }
        public string CustomPawnPrompt { get; set; } = string.Empty;
        public string CustomMapPrompt { get; set; } = string.Empty;
        public FlywheelAutoApplyMode AutoApplyMode { get; set; } = FlywheelAutoApplyMode.Off;
        public float AutoApplyConfidenceThreshold { get; set; } = 0.8f;
        public AgentAutonomyLevel AutonomyLevel { get; set; } = AgentAutonomyLevel.Autonomous;
        public bool ShouldApproveAction(RiskLevel risk) => true;

        public IContextSettings Context => this;
        public float ContextBudget { get; set; } = 1000f;
        public int ContextBriefLimit => 200;
        public int EnvironmentScanRadius => 15;
        public int EnvironmentMaxItems => 10;
        public float ThreatThresholdHigh => 0.8f;
        public float ThreatThresholdMedium => 0.5f;
        public float ThreatThresholdLow => 0.2f;
        public int MaxCacheEntries => 100;
        public float MoodDiffThreshold => 0.1f;
        public float TemperatureDiffThreshold => 5f;
        public bool IncludeRace { get; set; } = true;
        public bool IncludeAge { get; set; } = true;
        public bool IncludeGender { get; set; } = true;
        public bool IncludeBackstory { get; set; } = true;
        public bool IncludeIdeology { get; set; }
        public bool IncludeTraits { get; set; } = true;
        public bool IncludeSkills { get; set; } = true;
        public int MinSkillLevel { get; set; }
        public bool IncludeHealth { get; set; } = true;
        public bool IncludeCapacities { get; set; } = true;
        public bool IncludeMood { get; set; } = true;
        public bool IncludeMoodThoughts { get; set; }
        public bool IncludeCurrentJob { get; set; } = true;
        public bool IncludeWorkPriorities { get; set; } = true;
        public bool IncludeEquipment { get; set; } = true;
        public bool IncludeInventory { get; set; }
        public bool IncludeLocation { get; set; }
        public bool IncludeRelations { get; set; } = true;
        public bool IncludeGenes { get; set; } = true;
        public bool IncludeSurroundings { get; set; }
        public bool IncludeCombatStatus { get; set; } = true;
        public bool IncludeGameTime { get; set; } = true;
        public bool IncludeColonistCount { get; set; } = true;
        public bool IncludeColonistNames { get; set; } = true;
        public bool IncludeWealth { get; set; }
        public bool IncludeFood { get; set; } = true;
        public bool IncludeSeason { get; set; } = true;
        public bool IncludeWeather { get; set; } = true;
        public bool IncludeThreats { get; set; } = true;

        public void ApplyPreset(ContextPreset preset) { }
        public void ResetToDefault() { }
    }
}
