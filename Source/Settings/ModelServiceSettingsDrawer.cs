using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimMind.ModelService.Diagnostics;
using RimMind.ModelService.Models;
using RimMind.ModelService.Protocol;
using RimMind.Presentation.Api;
using RimMind.Presentation.UI;
using UnityEngine;
using Verse;

namespace RimMind.ModelService.Settings
{
    public static class ModelServiceSettingsDrawer
    {
        private const float CardHeight = 160f;
        private const float CardGap = 10f;

        private static Vector2 _scrollPosition = Vector2.zero;
        private static float _measuredHeight = 600f;
        private static readonly HashSet<string> _revealedKeyNodeIds = new HashSet<string>();
        private static readonly ConcurrentDictionary<string, bool> _probingNodes = new ConcurrentDictionary<string, bool>();

        public static void DrawSettings(Rect inRect, ModelServiceSettings settings)
        {
            if (settings == null) return;

            SettingsUIDrawer.DrawScrollableSettingsPage(
                inRect,
                ref _scrollPosition,
                ref _measuredHeight,
                listing => DrawContent(listing, settings),
                onReset: () => settings.ResetToDefault(),
                onDirtySave: () => settings.Write());
        }

        private static void DrawContent(Listing_Standard listing, ModelServiceSettings settings)
        {
            DrawGeneralSection(listing, settings);
            DrawToolbar(listing, settings);
            DrawEndpointsList(listing, settings);
        }

        private static void DrawGeneralSection(Listing_Standard listing, ModelServiceSettings settings)
        {
            SettingsUIDrawer.DrawSectionHeader(
                listing,
                RimMindText.TrOr("RimMind.ModelService.Settings.Category", "Model Service"),
                RimMindText.TrOr(
                    "RimMind.ModelService.Settings.EnableService.Desc",
                    "When enabled, manages and expands the AI request pipeline with multi-endpoint routing and automatic failover."));

            listing.CheckboxLabeled(
                RimMindText.TrOr("RimMind.ModelService.Settings.EnableService", "Enable Extended Model Service"),
                ref settings.enableService,
                RimMindText.TrOr(
                    "RimMind.ModelService.Settings.EnableService.Desc",
                    "When enabled, manages and expands the AI request pipeline with multi-endpoint routing and automatic failover."));
            listing.Gap(10f);

            SettingsUIDrawer.DrawSectionHeader(
                listing,
                RimMindText.TrOr("RimMind.ModelService.Settings.BalancingStrategy", "Load Balancing Strategy"));

            bool isPriority = settings.balancingStrategy == BalancingStrategy.PriorityFailover;
            if (listing.RadioButton(
                    RimMindText.TrOr("RimMind.ModelService.Settings.BalancingStrategy.PriorityFailover", "Priority Failover"),
                    isPriority,
                    tooltip: RimMindText.TrOr(
                        "RimMind.ModelService.Settings.BalancingStrategy.PriorityFailover.Desc",
                        "Prefer higher priority healthy nodes; automatically fail over to backups on failure.")) &&
                settings.balancingStrategy != BalancingStrategy.PriorityFailover)
            {
                settings.balancingStrategy = BalancingStrategy.PriorityFailover;
                GUI.changed = true;
            }

            bool isRoundRobin = settings.balancingStrategy == BalancingStrategy.RoundRobin;
            if (listing.RadioButton(
                    RimMindText.TrOr("RimMind.ModelService.Settings.BalancingStrategy.RoundRobin", "Round Robin"),
                    isRoundRobin,
                    tooltip: RimMindText.TrOr(
                        "RimMind.ModelService.Settings.BalancingStrategy.RoundRobin.Desc",
                        "Distribute requests across all healthy nodes according to weights.")) &&
                settings.balancingStrategy != BalancingStrategy.RoundRobin)
            {
                settings.balancingStrategy = BalancingStrategy.RoundRobin;
                GUI.changed = true;
            }
            listing.Gap(14f);
        }

        private static void DrawToolbar(Listing_Standard listing, ModelServiceSettings settings)
        {
            SettingsUIDrawer.DrawSectionHeader(
                listing,
                RimMindText.TrOr("RimMind.ModelService.Settings.EndpointsSection", "Model Endpoint Pool"));

            var buttonRow = listing.GetRect(30f);
            float curBtnX = buttonRow.x;
            float curBtnY = buttonRow.y;
            float rowMaxX = buttonRow.xMax;

            Rect NextButtonRect(float width)
            {
                if (curBtnX > buttonRow.x && curBtnX + width > rowMaxX)
                {
                    var nextRow = listing.GetRect(34f);
                    curBtnX = nextRow.x;
                    curBtnY = nextRow.y + 4f;
                }
                var rect = new Rect(curBtnX, curBtnY, width, 30f);
                curBtnX += width + 5f;
                return rect;
            }

            var codexRect = NextButtonRect(140f);
            if (Widgets.ButtonText(codexRect, RimMindText.TrOr("RimMind.ModelService.Settings.AddCodexPreset", "+ Codex Preset")))
            {
                lock (settings.EndpointSyncRoot)
                {
                    settings.endpoints.Add(ModelEndpointPresets.CreateCodexGatewayPreset(settings.endpoints.Count));
                }
                GUI.changed = true;
            }
            TooltipHandler.TipRegion(
                codexRect,
                RimMindText.TrOr("RimMind.ModelService.Settings.AddCodexPreset.Desc", "Quickly add local Codex gateway endpoint preset."));

            var openCodeRect = NextButtonRect(160f);
            if (Widgets.ButtonText(openCodeRect, RimMindText.TrOr("RimMind.ModelService.Settings.AddOpenCodeGoPreset", "+ OpenCode Go Preset")))
            {
                lock (settings.EndpointSyncRoot)
                {
                    settings.endpoints.Add(ModelEndpointPresets.CreateOpenCodeGoPreset(settings.endpoints.Count));
                }
                GUI.changed = true;
            }
            TooltipHandler.TipRegion(
                openCodeRect,
                RimMindText.TrOr("RimMind.ModelService.Settings.AddOpenCodeGoPreset.Desc", "Quickly add OpenCode Go subscription endpoint preset."));

            var deepSeekRect = NextButtonRect(145f);
            if (Widgets.ButtonText(deepSeekRect, RimMindText.TrOr("RimMind.ModelService.Settings.AddDeepSeekPreset", "+ DeepSeek Preset")))
            {
                lock (settings.EndpointSyncRoot)
                {
                    settings.endpoints.Add(ModelEndpointPresets.CreateDeepSeekPreset(settings.endpoints.Count));
                }
                GUI.changed = true;
            }
            TooltipHandler.TipRegion(
                deepSeekRect,
                RimMindText.TrOr(
                    "RimMind.ModelService.Settings.AddDeepSeekPreset.Desc",
                    "Quickly add DeepSeek official API node with adaptive thinking & high safety mode."));

            var customRect = NextButtonRect(130f);
            if (Widgets.ButtonText(customRect, RimMindText.TrOr("RimMind.ModelService.Settings.AddCustomEndpoint", "+ Custom Node")))
            {
                lock (settings.EndpointSyncRoot)
                {
                    var defaultProtocol = ModelProtocolRegistry.Resolve(ModelProtocolRegistry.OpenAIProtocolId);
                    settings.endpoints.Add(new ModelEndpointConfig
                    {
                        name = $"Node {settings.endpoints.Count + 1}",
                        endpoint = "http://127.0.0.1:8000/v1",
                        modelName = defaultProtocol.DefaultModel,
                        providerType = defaultProtocol.LegacyProviderType,
                        protocolId = defaultProtocol.ProtocolId,
                        priority = settings.endpoints.Count,
                        weight = 1,
                        isEnabled = true
                    });
                }
                GUI.changed = true;
            }
            TooltipHandler.TipRegion(
                customRect,
                RimMindText.TrOr("RimMind.ModelService.Settings.AddCustomEndpoint.Desc", "Add a custom model service endpoint configuration."));

            var testAllRect = NextButtonRect(130f);
            if (Widgets.ButtonText(testAllRect, RimMindText.TrOr("RimMind.ModelService.Settings.TestAllEndpoints", "Test All")))
            {
                TriggerTestAllEndpoints(settings);
            }
            TooltipHandler.TipRegion(
                testAllRect,
                RimMindText.TrOr("RimMind.ModelService.Settings.TestAllEndpoints.Desc", "Concurrently ping and probe health for all enabled endpoints."));

            listing.Gap(10f);
        }

        private static void DrawEndpointsList(Listing_Standard listing, ModelServiceSettings settings)
        {
            var endpointsSnapshot = settings.GetEndpointsSnapshot();
            if (endpointsSnapshot.Length == 0)
            {
                Rect emptyRect = listing.GetRect(30f);
                Widgets.Label(
                    emptyRect,
                    RimMindText.TrOr(
                        "RimMind.ModelService.Settings.NoEndpoints",
                        "Endpoint pool is empty. Click the buttons above to add presets or a custom node."));
                return;
            }

            int deleteIndex = -1;
            int moveUpIndex = -1;
            int moveDownIndex = -1;

            for (int i = 0; i < endpointsSnapshot.Length; i++)
            {
                var endpoint = endpointsSnapshot[i];
                if (endpoint == null) continue;

                Rect cardRect = listing.GetRect(CardHeight);
                DrawEndpointCard(
                    cardRect,
                    settings,
                    endpoint,
                    i,
                    endpointsSnapshot.Length,
                    ref deleteIndex,
                    ref moveUpIndex,
                    ref moveDownIndex);
                listing.Gap(CardGap);
            }

            bool mutatedList = false;
            lock (settings.EndpointSyncRoot)
            {
                if (deleteIndex >= 0 && deleteIndex < settings.endpoints.Count)
                {
                    settings.endpoints.RemoveAt(deleteIndex);
                    SyncPriorities(settings.endpoints);
                    mutatedList = true;
                }
                else if (moveUpIndex > 0 && moveUpIndex < settings.endpoints.Count)
                {
                    var temp = settings.endpoints[moveUpIndex];
                    settings.endpoints[moveUpIndex] = settings.endpoints[moveUpIndex - 1];
                    settings.endpoints[moveUpIndex - 1] = temp;
                    SyncPriorities(settings.endpoints);
                    mutatedList = true;
                }
                else if (moveDownIndex >= 0 && moveDownIndex < settings.endpoints.Count - 1)
                {
                    var temp = settings.endpoints[moveDownIndex];
                    settings.endpoints[moveDownIndex] = settings.endpoints[moveDownIndex + 1];
                    settings.endpoints[moveDownIndex + 1] = temp;
                    SyncPriorities(settings.endpoints);
                    mutatedList = true;
                }
            }

            if (mutatedList)
            {
                GUI.changed = true;
            }
        }

        private static void DrawEndpointCard(
            Rect cardRect,
            ModelServiceSettings settings,
            ModelEndpointConfig endpoint,
            int index,
            int totalCount,
            ref int deleteIndex,
            ref int moveUpIndex,
            ref int moveDownIndex)
        {
            Widgets.DrawMenuSection(cardRect);

            float leftX = cardRect.x + 10f;
            float rightXMax = cardRect.xMax - 10f;

            // --- Row 1: Priority indicator, Name field, Enable checkbox, Move Up, Move Down, Delete ---
            float row1Y = cardRect.y + 8f;

            string priorityText = index == 0
                ? RimMindText.TrOr("RimMind.ModelService.Endpoint.PrimaryNode", "#{0} Primary", index + 1)
                : RimMindText.TrOr("RimMind.ModelService.Endpoint.BackupNode", "#{0} Backup", index + 1);
            Rect priorityRect = new Rect(leftX, row1Y + 2f, 85f, 24f);
            Widgets.Label(priorityRect, priorityText);
            TooltipHandler.TipRegion(priorityRect, priorityText);

            float nameLabelX = leftX + 90f;
            string nameLabel = RimMindText.TrOr("RimMind.ModelService.Endpoint.NameLabel", "Name:");
            Widgets.Label(new Rect(nameLabelX, row1Y + 2f, 40f, 24f), nameLabel);
            var nameRect = new Rect(nameLabelX + 45f, row1Y, 160f, 24f);
            endpoint.name = Widgets.TextField(nameRect, endpoint.name ?? string.Empty);
            TooltipHandler.TipRegion(nameRect, nameLabel);

            float chkX = nameLabelX + 215f;
            Rect chkRect = new Rect(chkX, row1Y, 80f, 24f);
            Widgets.CheckboxLabeled(
                chkRect,
                RimMindText.TrOr("RimMind.ModelService.Endpoint.Enabled", "Enabled"),
                ref endpoint.isEnabled);
            TooltipHandler.TipRegion(
                chkRect,
                RimMindText.TrOr("RimMind.ModelService.Endpoint.Enabled.Desc", "Check to include this node in the routing pool; unchecked nodes are ignored."));

            string removeLabel = RimMindText.TrOr("RimMind.ModelService.Settings.RemoveEndpoint", "Delete");
            var delRect = new Rect(rightXMax - 60f, row1Y, 60f, 24f);
            if (Widgets.ButtonText(delRect, removeLabel))
            {
                deleteIndex = index;
            }
            TooltipHandler.TipRegion(delRect, removeLabel);

            var downRect = new Rect(rightXMax - 96f, row1Y, 30f, 24f);
            if (index < totalCount - 1)
            {
                if (Widgets.ButtonText(downRect, "▼"))
                {
                    moveDownIndex = index;
                }
                TooltipHandler.TipRegion(downRect, RimMindText.TrOr("RimMind.ModelService.Endpoint.MoveDown", "Move Down"));
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, 0.3f);
                Widgets.ButtonText(downRect, "▼");
                GUI.color = Color.white;
            }

            var upRect = new Rect(rightXMax - 132f, row1Y, 30f, 24f);
            if (index > 0)
            {
                if (Widgets.ButtonText(upRect, "▲"))
                {
                    moveUpIndex = index;
                }
                TooltipHandler.TipRegion(upRect, RimMindText.TrOr("RimMind.ModelService.Endpoint.MoveUp", "Move Up"));
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, 0.3f);
                Widgets.ButtonText(upRect, "▲");
                GUI.color = Color.white;
            }

            // --- Row 2: Protocol selection button (FloatMenu), URL text field ---
            float row2Y = cardRect.y + 42f;
            var currentProtocol = ModelProtocolRegistry.Resolve(endpoint);
            string providerLabel = GetProtocolDisplayLabel(currentProtocol);

            var provRect = new Rect(leftX, row2Y, 210f, 26f);
            if (Widgets.ButtonText(provRect, providerLabel))
            {
                var capturedEndpoint = endpoint;
                var options = new List<FloatMenuOption>();
                foreach (var protocol in ModelProtocolRegistry.All)
                {
                    var capturedProtocol = protocol;
                    string optionLabel = GetProtocolDisplayLabel(capturedProtocol);
                    options.Add(new FloatMenuOption(optionLabel, () =>
                    {
                        ApplyProtocolSelection(capturedEndpoint, capturedProtocol);
                        settings.Write();
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            TooltipHandler.TipRegion(provRect, providerLabel);

            float urlLabelX = leftX + 220f;
            Widgets.Label(new Rect(urlLabelX, row2Y + 2f, 38f, 24f), "URL:");
            var urlFieldRect = new Rect(urlLabelX + 42f, row2Y, rightXMax - (urlLabelX + 42f), 24f);
            endpoint.endpoint = Widgets.TextField(urlFieldRect, endpoint.endpoint ?? string.Empty);
            TooltipHandler.TipRegion(
                urlFieldRect,
                RimMindText.TrOr("RimMind.ModelService.Endpoint.Url.Desc", "Complete API base URL for this endpoint (e.g. http://127.0.0.1:8000/v1)."));

            // --- Row 3: Model text field, API Key secret field via shared SettingsUIDrawer.DrawSecretField ---
            float row3Y = cardRect.y + 76f;

            Widgets.Label(
                new Rect(leftX, row3Y + 2f, 50f, 24f),
                RimMindText.TrOr("RimMind.ModelService.Endpoint.ModelLabel", "Model:"));
            var modelFieldRect = new Rect(leftX + 55f, row3Y, 170f, 24f);
            endpoint.modelName = Widgets.TextField(modelFieldRect, endpoint.modelName ?? string.Empty);
            TooltipHandler.TipRegion(
                modelFieldRect,
                RimMindText.TrOr(
                    "RimMind.ModelService.Endpoint.Model.Desc",
                    "Model identifier passed when querying this endpoint (e.g. gpt-4o-mini, deepseek-flash, or deepseek-v4-pro)."));

            float keyLabelX = leftX + 235f;
            Widgets.Label(new Rect(keyLabelX, row3Y + 2f, 65f, 24f), "API Key:");

            bool isRevealed = _revealedKeyNodeIds.Contains(endpoint.id);
            string currentApiKey = endpoint.apiKey ?? string.Empty;
            var secretRowRect = new Rect(keyLabelX + 70f, row3Y, rightXMax - (keyLabelX + 70f), 24f);
            SettingsUIDrawer.DrawSecretField(
                secretRowRect,
                ref currentApiKey,
                ref isRevealed,
                emptyPlaceholder: RimMindText.TrOr("RimMind.Settings.ApiKey.EmptyPlaceholder", "Click to enter API key..."),
                savedFormat: RimMindText.TrOr("RimMind.Settings.ApiKey.Saved", "Saved ({0} chars)"),
                showLabel: RimMindText.TrOr("RimMind.ModelService.Endpoint.ShowKey", "Show"),
                hideLabel: RimMindText.TrOr("RimMind.ModelService.Endpoint.HideKey", "Hide"),
                tooltip: RimMindText.TrOr(
                    "RimMind.ModelService.Endpoint.ApiKey.Desc",
                    "API key or token for this endpoint. Leave empty if authentication is not required."),
                toggleButtonWidth: 65f);

            if (!string.Equals(endpoint.apiKey ?? string.Empty, currentApiKey, StringComparison.Ordinal))
            {
                endpoint.apiKey = currentApiKey;
                GUI.changed = true;
            }
            if (isRevealed) _revealedKeyNodeIds.Add(endpoint.id);
            else _revealedKeyNodeIds.Remove(endpoint.id);

            // --- Row 4: Single-node Ping button, Status indicator, Capability-driven protocol controls/banner ---
            float row4Y = cardRect.y + 110f;
            DrawHealthRow(leftX, rightXMax, row4Y, settings, endpoint, currentProtocol);
        }

        private static void DrawHealthRow(
            float leftX,
            float rightXMax,
            float row4Y,
            ModelServiceSettings settings,
            ModelEndpointConfig endpoint,
            IModelProtocol currentProtocol)
        {
            bool isProbing = _probingNodes.ContainsKey(endpoint.id);
            var pingBtnRect = new Rect(leftX, row4Y, 90f, 26f);
            if (isProbing)
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(pingBtnRect, RimMindText.TrOr("RimMind.ModelService.Endpoint.StatusTesting", "Probing..."));
                GUI.color = Color.white;
            }
            else if (Widgets.ButtonText(pingBtnRect, RimMindText.TrOr("RimMind.ModelService.Endpoint.TestPing", "Ping Test")))
            {
                _probingNodes[endpoint.id] = true;
                Task.Run(async () =>
                {
                    try
                    {
                        await EndpointHealthProbe.ProbeEndpointAsync(endpoint);
                    }
                    finally
                    {
                        _probingNodes.TryRemove(endpoint.id, out _);
                    }
                });
            }
            TooltipHandler.TipRegion(
                pingBtnRect,
                RimMindText.TrOr("RimMind.ModelService.Endpoint.TestPing.Desc", "Send a lightweight test probe to verify network reachability and latency."));

            float statusX = leftX + 100f;
            string statusIcon = isProbing
                ? "⚪"
                : (endpoint.lastLatencyMs < 0
                    ? (endpoint.isHealthy ? "⚪" : "🔴")
                    : (endpoint.isHealthy ? "🟢" : "🔴"));

            string latencyStr = endpoint.lastLatencyMs >= 0 ? $"{endpoint.lastLatencyMs} ms" : "--";
            string statusDetail = endpoint.isHealthy
                ? RimMindText.TrOr("RimMind.ModelService.Endpoint.StatusHealthy", "Healthy")
                : RimMindText.TrOr("RimMind.ModelService.Endpoint.StatusUnhealthy", "Unhealthy");

            var statusRect = new Rect(statusX, row4Y + 2f, 155f, 24f);
            string statusText = $"{statusIcon} {latencyStr} ({statusDetail})";
            Widgets.Label(statusRect, statusText);
            TooltipHandler.TipRegion(statusRect, statusText);

            float controlsX = statusX + 165f;
            float availW = rightXMax - controlsX;
            DrawProtocolCapabilitiesControls(controlsX, row4Y, availW, settings, endpoint, currentProtocol.Capabilities);
        }

        private static void DrawProtocolCapabilitiesControls(
            float controlsX,
            float row4Y,
            float availW,
            ModelServiceSettings settings,
            ModelEndpointConfig endpoint,
            ProtocolCapabilities caps)
        {
            if (availW <= 0f || caps == null) return;

            if (caps.RequiresLoopback)
            {
                bool isLoopback = endpoint.IsLoopbackAddress;
                string bannerText = isLoopback
                    ? RimMindText.TrOr("RimMind.ModelService.Security.LoopbackLocked", "Local Loopback Secured")
                    : RimMindText.TrOr("RimMind.ModelService.Security.LoopbackBlocked", "Security Blocked");

                var bannerRect = new Rect(controlsX, row4Y + 2f, availW, 24f);
                Color prevColor = GUI.color;
                GUI.color = isLoopback ? new Color(0.35f, 0.95f, 0.45f) : new Color(1f, 0.35f, 0.35f);
                Widgets.Label(bannerRect, bannerText);
                GUI.color = prevColor;

                string tip = isLoopback
                    ? RimMindText.TrOr(
                        "RimMind.ModelService.Security.LoopbackLocked.Desc",
                        "Endpoint is bound to local loopback (127.0.0.1 / localhost), operating within a protected subscription gateway sandbox.")
                    : RimMindText.TrOr(
                        "RimMind.ModelService.Security.LoopbackViolation",
                        "Security Warning: Subscription proxy provider must bind to loopback address (127.0.0.1 / localhost) to prevent credential leakage.");
                TooltipHandler.TipRegion(bannerRect, tip);
                return;
            }

            if (caps.SupportsNativeThinking || caps.SupportsSafetyCheck)
            {
                if (availW < 180f) return;

                bool showBoth = caps.SupportsNativeThinking && caps.SupportsSafetyCheck;
                float btnW = showBoth ? Math.Min(175f, (availW - 8f) / 2f) : Math.Min(175f, availW);
                float nextX = controlsX;

                if (caps.SupportsNativeThinking)
                {
                    var thinkingRect = new Rect(nextX, row4Y, btnW, 24f);
                    string thinkingLabel = GetThinkingPolicyLabel(endpoint.thinkingPolicy);
                    if (Widgets.ButtonText(thinkingRect, thinkingLabel))
                    {
                        var captured = endpoint;
                        var opts = new List<FloatMenuOption>
                        {
                            new FloatMenuOption(
                                GetThinkingPolicyLabel(EndpointThinkingPolicy.FollowRequest),
                                () => { captured.thinkingPolicy = EndpointThinkingPolicy.FollowRequest; settings.Write(); }),
                            new FloatMenuOption(
                                GetThinkingPolicyLabel(EndpointThinkingPolicy.ForceEnabled),
                                () => { captured.thinkingPolicy = EndpointThinkingPolicy.ForceEnabled; settings.Write(); }),
                            new FloatMenuOption(
                                GetThinkingPolicyLabel(EndpointThinkingPolicy.ForceDisabled),
                                () => { captured.thinkingPolicy = EndpointThinkingPolicy.ForceDisabled; settings.Write(); }),
                        };
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                    TooltipHandler.TipRegion(
                        thinkingRect,
                        RimMindText.TrOr(
                            "RimMind.ModelService.Endpoint.Thinking.Desc",
                            "Controls native thinking mode. Auto follows per-scenario intent (off for dialogue, enabled for autonomous actions and advisor)."));
                    nextX += btnW + 8f;
                }

                if (caps.SupportsSafetyCheck)
                {
                    var safetyRect = new Rect(nextX, row4Y, btnW, 24f);
                    string safetyLabel = GetSafetyLevelLabel(endpoint.safetyLevel);
                    if (Widgets.ButtonText(safetyRect, safetyLabel))
                    {
                        var captured = endpoint;
                        var opts = new List<FloatMenuOption>
                        {
                            new FloatMenuOption(
                                GetSafetyLevelLabel(EndpointSafetyLevel.Standard),
                                () => { captured.safetyLevel = EndpointSafetyLevel.Standard; settings.Write(); }),
                            new FloatMenuOption(
                                GetSafetyLevelLabel(EndpointSafetyLevel.High),
                                () => { captured.safetyLevel = EndpointSafetyLevel.High; settings.Write(); }),
                        };
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                    TooltipHandler.TipRegion(
                        safetyRect,
                        RimMindText.TrOr(
                            "RimMind.ModelService.Endpoint.Safety.Desc",
                            "Controls content safety_check level. High sends safety_check.level=high and omits temperature per official guidelines."));
                }
                return;
            }

            if (caps.HasStatusBadge)
            {
                string bannerText = !string.IsNullOrEmpty(caps.StatusBadgeLabelKey)
                    ? RimMindText.TrOr(caps.StatusBadgeLabelKey!, caps.StatusBadgeFallback ?? string.Empty)
                    : (caps.StatusBadgeFallback ?? string.Empty);
                var bannerRect = new Rect(controlsX, row4Y + 2f, availW, 24f);

                Color prevColor = GUI.color;
                GUI.color = new Color(0.4f, 0.75f, 1.0f);
                Widgets.Label(bannerRect, bannerText);
                GUI.color = prevColor;

                string tip = !string.IsNullOrEmpty(caps.StatusBadgeTooltipKey)
                    ? RimMindText.TrOr(caps.StatusBadgeTooltipKey!, caps.StatusBadgeTooltipFallback ?? string.Empty)
                    : (caps.StatusBadgeTooltipFallback ?? string.Empty);
                if (!string.IsNullOrEmpty(tip))
                {
                    TooltipHandler.TipRegion(bannerRect, tip);
                }
            }
        }

        private static string GetThinkingPolicyLabel(EndpointThinkingPolicy policy) => policy switch
        {
            EndpointThinkingPolicy.ForceEnabled => RimMindText.TrOr("RimMind.ModelService.Endpoint.Thinking.ForceEnabled", "Thinking: On"),
            EndpointThinkingPolicy.ForceDisabled => RimMindText.TrOr("RimMind.ModelService.Endpoint.Thinking.ForceDisabled", "Thinking: Off"),
            _ => RimMindText.TrOr("RimMind.ModelService.Endpoint.Thinking.FollowRequest", "Thinking: Auto"),
        };

        private static string GetSafetyLevelLabel(EndpointSafetyLevel level) => level switch
        {
            EndpointSafetyLevel.High => RimMindText.TrOr("RimMind.ModelService.Endpoint.Safety.High", "Safety: High"),
            _ => RimMindText.TrOr("RimMind.ModelService.Endpoint.Safety.Standard", "Safety: Standard"),
        };

        internal static string GetProtocolDisplayLabel(IModelProtocol protocol)
        {
            if (protocol == null) return string.Empty;
            string fallback = !string.IsNullOrEmpty(protocol.FallbackLabel) ? protocol.FallbackLabel : protocol.ProtocolId;
            return !string.IsNullOrEmpty(protocol.DisplayLabelKey)
                ? RimMindText.TrOr(protocol.DisplayLabelKey, fallback)
                : fallback;
        }

        internal static void ApplyProtocolSelection(ModelEndpointConfig endpoint, IModelProtocol selectedProtocol)
        {
            if (endpoint == null || selectedProtocol == null) return;

            var previousProtocol = ModelProtocolRegistry.Resolve(endpoint);
            bool updateUrl = string.IsNullOrWhiteSpace(endpoint.endpoint)
                || string.Equals(endpoint.endpoint, previousProtocol.DefaultEndpointUrl, StringComparison.OrdinalIgnoreCase);
            bool updateModel = string.IsNullOrWhiteSpace(endpoint.modelName)
                || string.Equals(endpoint.modelName, previousProtocol.DefaultModel, StringComparison.OrdinalIgnoreCase);

            endpoint.protocolId = selectedProtocol.ProtocolId;
            endpoint.providerType = selectedProtocol.LegacyProviderType;

            if (updateUrl && !string.IsNullOrWhiteSpace(selectedProtocol.DefaultEndpointUrl))
            {
                endpoint.endpoint = selectedProtocol.DefaultEndpointUrl;
            }

            if (updateModel && !string.IsNullOrWhiteSpace(selectedProtocol.DefaultModel))
            {
                endpoint.modelName = selectedProtocol.DefaultModel;
            }
        }

        private static void SyncPriorities(List<ModelEndpointConfig> endpoints)
        {
            for (int i = 0; i < endpoints.Count; i++)
            {
                endpoints[i].priority = i;
            }
        }

        private static void TriggerTestAllEndpoints(ModelServiceSettings settings)
        {
            var snapshot = settings.GetEndpointsSnapshot();
            foreach (var node in snapshot)
            {
                var targetNode = node;
                if (targetNode == null) continue;
                _probingNodes[targetNode.id] = true;
                Task.Run(async () =>
                {
                    try
                    {
                        await EndpointHealthProbe.ProbeEndpointAsync(targetNode);
                    }
                    finally
                    {
                        _probingNodes.TryRemove(targetNode.id, out _);
                    }
                });
            }
        }
    }
}
