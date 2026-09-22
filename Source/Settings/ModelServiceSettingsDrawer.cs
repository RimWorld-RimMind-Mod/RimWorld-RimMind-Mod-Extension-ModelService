using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimMind.ModelService.Diagnostics;
using RimMind.ModelService.Models;
using RimMind.ModelService.Security;
using RimMind.Presentation.UI;
using UnityEngine;
using Verse;

namespace RimMind.ModelService.Settings
{
    public static class ModelServiceSettingsDrawer
    {
        private static Vector2 _scrollPosition = Vector2.zero;
        private static readonly HashSet<string> _revealedKeyNodeIds = new HashSet<string>();
        private static readonly ConcurrentDictionary<string, bool> _probingNodes = new ConcurrentDictionary<string, bool>();

        public static void DrawSettings(Rect inRect, ModelServiceSettings settings)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            // 1. Enable Service Section & Checkbox
            SettingsUIDrawer.DrawSectionHeader(
                listing,
                "RimMind.ModelService.Settings.Category".Translate(),
                "RimMind.ModelService.Settings.EnableService.Desc".Translate());

            listing.CheckboxLabeled(
                "RimMind.ModelService.Settings.EnableService".Translate(),
                ref settings.enableService,
                "RimMind.ModelService.Settings.EnableService.Desc".Translate());
            listing.Gap(10f);

            // 2. Balancing Strategy Section & Radio Buttons
            SettingsUIDrawer.DrawSectionHeader(
                listing,
                "RimMind.ModelService.Settings.BalancingStrategy".Translate());

            bool isPriority = settings.balancingStrategy == BalancingStrategy.PriorityFailover;
            if (listing.RadioButton(
                "RimMind.ModelService.Settings.BalancingStrategy.PriorityFailover".Translate(),
                isPriority,
                tooltip: "RimMind.ModelService.Settings.BalancingStrategy.PriorityFailover.Desc".Translate()))
            {
                settings.balancingStrategy = BalancingStrategy.PriorityFailover;
            }

            bool isRoundRobin = settings.balancingStrategy == BalancingStrategy.RoundRobin;
            if (listing.RadioButton(
                "RimMind.ModelService.Settings.BalancingStrategy.RoundRobin".Translate(),
                isRoundRobin,
                tooltip: "RimMind.ModelService.Settings.BalancingStrategy.RoundRobin.Desc".Translate()))
            {
                settings.balancingStrategy = BalancingStrategy.RoundRobin;
            }
            listing.Gap(14f);

            // 3. Top Action Buttons Row
            SettingsUIDrawer.DrawSectionHeader(
                listing,
                "RimMind.ModelService.Settings.EndpointsSection".Translate());
            var buttonRow = listing.GetRect(30f);
            float curBtnX = buttonRow.x;

            // [+ Codex 预设] (140px)
            var codexRect = new Rect(curBtnX, buttonRow.y, 140f, 30f);
            if (Widgets.ButtonText(codexRect, "RimMind.ModelService.Settings.AddCodexPreset".Translate()))
            {
                var preset = ModelEndpointPresets.CreateCodexGatewayPreset(settings.endpoints.Count);
                settings.endpoints.Add(preset);
            }
            TooltipHandler.TipRegion(codexRect, "RimMind.ModelService.Settings.AddCodexPreset.Desc".Translate());
            curBtnX += 145f;

            // [+ OpenCode Go 预设] (160px)
            var openCodeRect = new Rect(curBtnX, buttonRow.y, 160f, 30f);
            if (Widgets.ButtonText(openCodeRect, "RimMind.ModelService.Settings.AddOpenCodeGoPreset".Translate()))
            {
                var preset = ModelEndpointPresets.CreateOpenCodeGoPreset(settings.endpoints.Count);
                settings.endpoints.Add(preset);
            }
            TooltipHandler.TipRegion(openCodeRect, "RimMind.ModelService.Settings.AddOpenCodeGoPreset.Desc".Translate());
            curBtnX += 165f;

            // [+ 自定义节点] (130px)
            var customRect = new Rect(curBtnX, buttonRow.y, 130f, 30f);
            if (Widgets.ButtonText(customRect, "RimMind.ModelService.Settings.AddCustomEndpoint".Translate()))
            {
                settings.endpoints.Add(new ModelEndpointConfig
                {
                    name = $"Node {settings.endpoints.Count + 1}",
                    endpoint = "http://127.0.0.1:8000/v1",
                    modelName = "gpt-4o-mini",
                    providerType = ProviderType.OpenAICompatible,
                    priority = settings.endpoints.Count,
                    weight = 1,
                    isEnabled = true
                });
            }
            TooltipHandler.TipRegion(customRect, "RimMind.ModelService.Settings.AddCustomEndpoint.Desc".Translate());
            curBtnX += 135f;

            // [测试全部] (130px)
            var testAllRect = new Rect(curBtnX, buttonRow.y, 130f, 30f);
            if (Widgets.ButtonText(testAllRect, "RimMind.ModelService.Settings.TestAllEndpoints".Translate()))
            {
                TriggerTestAllEndpoints(settings);
            }
            TooltipHandler.TipRegion(testAllRect, "RimMind.ModelService.Settings.TestAllEndpoints.Desc".Translate());

            listing.Gap(10f);

            // 4. Endpoints Scroll View with Cards (Height ~160f per card)
            float remainingHeight = inRect.height - listing.CurHeight - 16f;
            var outRect = listing.GetRect(Math.Max(140f, remainingHeight));
            float cardHeight = 160f;
            float cardGap = 10f;
            float totalContentHeight = Math.Max(outRect.height, settings.endpoints.Count * (cardHeight + cardGap) + 20f);
            var viewRect = new Rect(0f, 0f, outRect.width - 24f, totalContentHeight);

            Widgets.BeginScrollView(outRect, ref _scrollPosition, viewRect);

            if (settings.endpoints.Count == 0)
            {
                var emptyRect = new Rect(10f, 20f, viewRect.width - 20f, 30f);
                GUI.color = Color.gray;
                Widgets.Label(emptyRect, "RimMind.ModelService.Settings.NoEndpoints".Translate());
                GUI.color = Color.white;
            }

            float curY = 0f;
            int deleteIndex = -1;
            int moveUpIndex = -1;
            int moveDownIndex = -1;

            for (int i = 0; i < settings.endpoints.Count; i++)
            {
                var endpoint = settings.endpoints[i];
                var cardRect = new Rect(0f, curY, viewRect.width, cardHeight);
                Widgets.DrawMenuSection(cardRect);

                float leftX = cardRect.x + 10f;
                float rightXMax = cardRect.xMax - 10f;

                // --- Row 1: Priority indicator, Name field, Enable checkbox, Move Up (▲), Move Down (▼), Delete button ---
                float row1Y = cardRect.y + 8f;

                // Priority indicator (#1 主节点 / #2 备用)
                string priorityText = (i == 0)
                    ? "RimMind.ModelService.Endpoint.PrimaryNode".Translate(i + 1)
                    : "RimMind.ModelService.Endpoint.BackupNode".Translate(i + 1);
                Rect priorityRect = new Rect(leftX, row1Y + 2f, 85f, 24f);
                Widgets.Label(priorityRect, priorityText);
                TooltipHandler.TipRegion(priorityRect, priorityText);

                // Name label & field
                float nameLabelX = leftX + 90f;
                Widgets.Label(new Rect(nameLabelX, row1Y + 2f, 40f, 24f), "RimMind.ModelService.Endpoint.NameLabel".Translate());
                var nameRect = new Rect(nameLabelX + 45f, row1Y, 160f, 24f);
                endpoint.name = Widgets.TextField(nameRect, endpoint.name);
                TooltipHandler.TipRegion(nameRect, "RimMind.ModelService.Endpoint.NameLabel".Translate());

                // Enable checkbox
                float chkX = nameLabelX + 215f;
                Rect chkRect = new Rect(chkX, row1Y, 80f, 24f);
                Widgets.CheckboxLabeled(chkRect, "RimMind.ModelService.Endpoint.Enabled".Translate(), ref endpoint.isEnabled);
                TooltipHandler.TipRegion(chkRect, "RimMind.ModelService.Endpoint.Enabled.Desc".Translate());

                // Row 1 right-side buttons: Move Up, Move Down, Delete
                var delRect = new Rect(rightXMax - 60f, row1Y, 60f, 24f);
                if (Widgets.ButtonText(delRect, "RimMind.ModelService.Settings.RemoveEndpoint".Translate()))
                {
                    deleteIndex = i;
                }
                TooltipHandler.TipRegion(delRect, "RimMind.ModelService.Settings.RemoveEndpoint".Translate());

                var downRect = new Rect(rightXMax - 96f, row1Y, 30f, 24f);
                if (i < settings.endpoints.Count - 1)
                {
                    if (Widgets.ButtonText(downRect, "▼"))
                    {
                        moveDownIndex = i;
                    }
                    TooltipHandler.TipRegion(downRect, "RimMind.ModelService.Endpoint.MoveDown".Translate());
                }
                else
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.3f);
                    Widgets.ButtonText(downRect, "▼");
                    GUI.color = Color.white;
                }

                var upRect = new Rect(rightXMax - 132f, row1Y, 30f, 24f);
                if (i > 0)
                {
                    if (Widgets.ButtonText(upRect, "▲"))
                    {
                        moveUpIndex = i;
                    }
                    TooltipHandler.TipRegion(upRect, "RimMind.ModelService.Endpoint.MoveUp".Translate());
                }
                else
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.3f);
                    Widgets.ButtonText(upRect, "▲");
                    GUI.color = Color.white;
                }

                // --- Row 2: Provider selection button (FloatMenu), URL text field ---
                float row2Y = cardRect.y + 42f;

                string providerLabel = endpoint.providerType switch
                {
                    ProviderType.OpenAICompatible => "RimMind.ModelService.Provider.OpenAICompatible".Translate(),
                    ProviderType.AnthropicClaude => "RimMind.ModelService.Provider.AnthropicClaude".Translate(),
                    ProviderType.LocalSubscriptionGateway => "RimMind.ModelService.Provider.LocalSubscriptionGateway".Translate(),
                    ProviderType.OpenCodeGo => "RimMind.ModelService.Provider.OpenCodeGo".Translate(),
                    _ => endpoint.providerType.ToString()
                };

                var provRect = new Rect(leftX, row2Y, 210f, 26f);
                if (Widgets.ButtonText(provRect, providerLabel))
                {
                    var options = new List<FloatMenuOption>
                    {
                        new FloatMenuOption("RimMind.ModelService.Provider.OpenAICompatible".Translate(), () => endpoint.providerType = ProviderType.OpenAICompatible),
                        new FloatMenuOption("RimMind.ModelService.Provider.AnthropicClaude".Translate(), () => endpoint.providerType = ProviderType.AnthropicClaude),
                        new FloatMenuOption("RimMind.ModelService.Provider.LocalSubscriptionGateway".Translate(), () => endpoint.providerType = ProviderType.LocalSubscriptionGateway),
                        new FloatMenuOption("RimMind.ModelService.Provider.OpenCodeGo".Translate(), () => endpoint.providerType = ProviderType.OpenCodeGo)
                    };
                    Find.WindowStack.Add(new FloatMenu(options));
                }
                TooltipHandler.TipRegion(provRect, providerLabel);

                float urlLabelX = leftX + 220f;
                Widgets.Label(new Rect(urlLabelX, row2Y + 2f, 38f, 24f), "URL:");
                var urlFieldRect = new Rect(urlLabelX + 42f, row2Y, rightXMax - (urlLabelX + 42f), 24f);
                endpoint.endpoint = Widgets.TextField(urlFieldRect, endpoint.endpoint);
                TooltipHandler.TipRegion(urlFieldRect, "RimMind.ModelService.Endpoint.Url.Desc".Translate());

                // --- Row 3: Model text field, API Key text field with Show/Hide toggle ---
                float row3Y = cardRect.y + 76f;

                Widgets.Label(new Rect(leftX, row3Y + 2f, 50f, 24f), "RimMind.ModelService.Endpoint.ModelLabel".Translate());
                var modelFieldRect = new Rect(leftX + 55f, row3Y, 170f, 24f);
                endpoint.modelName = Widgets.TextField(modelFieldRect, endpoint.modelName);
                TooltipHandler.TipRegion(modelFieldRect, "RimMind.ModelService.Endpoint.Model.Desc".Translate());

                float keyLabelX = leftX + 235f;
                Widgets.Label(new Rect(keyLabelX, row3Y + 2f, 65f, 24f), "API Key:");

                float toggleBtnWidth = 65f;
                var toggleBtnRect = new Rect(rightXMax - toggleBtnWidth, row3Y, toggleBtnWidth, 24f);
                bool isRevealed = _revealedKeyNodeIds.Contains(endpoint.id);
                string toggleLabel = isRevealed
                    ? "RimMind.ModelService.Endpoint.HideKey".Translate()
                    : "RimMind.ModelService.Endpoint.ShowKey".Translate();

                if (Widgets.ButtonText(toggleBtnRect, toggleLabel))
                {
                    if (isRevealed)
                    {
                        _revealedKeyNodeIds.Remove(endpoint.id);
                    }
                    else
                    {
                        _revealedKeyNodeIds.Add(endpoint.id);
                    }
                }
                TooltipHandler.TipRegion(toggleBtnRect, toggleLabel);

                var keyFieldRect = new Rect(keyLabelX + 70f, row3Y, toggleBtnRect.x - (keyLabelX + 75f), 24f);
                if (isRevealed)
                {
                    endpoint.apiKey = Widgets.TextField(keyFieldRect, endpoint.apiKey ?? "");
                }
                else
                {
                    endpoint.apiKey = GUI.PasswordField(keyFieldRect, endpoint.apiKey ?? "", '*');
                }
                TooltipHandler.TipRegion(keyFieldRect, "RimMind.ModelService.Endpoint.ApiKey.Desc".Translate());

                // --- Row 4: Single-node [Ping 测试] button, Latency text + Status Indicator (🟢 / 🔴 / ⚪), Security Banner ---
                float row4Y = cardRect.y + 110f;

                bool isProbing = _probingNodes.ContainsKey(endpoint.id);
                var pingBtnRect = new Rect(leftX, row4Y, 90f, 26f);
                if (isProbing)
                {
                    GUI.color = Color.gray;
                    Widgets.ButtonText(pingBtnRect, "RimMind.ModelService.Endpoint.StatusTesting".Translate());
                    GUI.color = Color.white;
                }
                else
                {
                    if (Widgets.ButtonText(pingBtnRect, "RimMind.ModelService.Endpoint.TestPing".Translate()))
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
                }
                TooltipHandler.TipRegion(pingBtnRect, "RimMind.ModelService.Endpoint.TestPing.Desc".Translate());

                float statusX = leftX + 100f;
                string statusIcon;
                if (isProbing)
                {
                    statusIcon = "⚪";
                }
                else if (endpoint.lastLatencyMs < 0)
                {
                    statusIcon = endpoint.isHealthy ? "⚪" : "🔴";
                }
                else
                {
                    statusIcon = endpoint.isHealthy ? "🟢" : "🔴";
                }

                string latencyStr = endpoint.lastLatencyMs >= 0 ? $"{endpoint.lastLatencyMs} ms" : "--";
                string statusDetail = endpoint.isHealthy
                    ? "RimMind.ModelService.Endpoint.StatusHealthy".Translate()
                    : "RimMind.ModelService.Endpoint.StatusUnhealthy".Translate();

                var statusRect = new Rect(statusX, row4Y + 2f, 155f, 24f);
                Widgets.Label(statusRect, $"{statusIcon} {latencyStr} ({statusDetail})");
                TooltipHandler.TipRegion(statusRect, $"{statusIcon} {latencyStr} ({statusDetail})");

                // Security Banner (if LocalSubscriptionGateway: 🛡️ 本地回环安全锁定 if loopback, or ⚠️ 凭据安全拦截 if not loopback)
                if (endpoint.providerType == ProviderType.LocalSubscriptionGateway)
                {
                    bool isLoopback = endpoint.IsLoopbackAddress;
                    string bannerText = isLoopback
                        ? "RimMind.ModelService.Security.LoopbackLocked".Translate()
                        : "RimMind.ModelService.Security.LoopbackBlocked".Translate();

                    float bannerX = statusX + 165f;
                    float bannerWidth = rightXMax - bannerX;
                    var bannerRect = new Rect(bannerX, row4Y + 2f, bannerWidth, 24f);

                    Color prevColor = GUI.color;
                    GUI.color = isLoopback ? new Color(0.35f, 0.95f, 0.45f) : new Color(1f, 0.35f, 0.35f);
                    Widgets.Label(bannerRect, bannerText);
                    GUI.color = prevColor;

                    string tip = isLoopback
                        ? "RimMind.ModelService.Security.LoopbackLocked.Desc".Translate()
                        : "RimMind.ModelService.Security.LoopbackViolation".Translate();
                    TooltipHandler.TipRegion(bannerRect, tip);
                }
                else if (endpoint.providerType == ProviderType.OpenCodeGo)
                {
                    string bannerText = "RimMind.ModelService.Security.OpenCodeGoSessionRouted".Translate();
                    float bannerX = statusX + 165f;
                    float bannerWidth = rightXMax - bannerX;
                    var bannerRect = new Rect(bannerX, row4Y + 2f, bannerWidth, 24f);

                    Color prevColor = GUI.color;
                    GUI.color = new Color(0.4f, 0.75f, 1.0f);
                    Widgets.Label(bannerRect, bannerText);
                    GUI.color = prevColor;

                    string tip = "RimMind.ModelService.Security.OpenCodeGoSessionRouted.Desc".Translate();
                    TooltipHandler.TipRegion(bannerRect, tip);
                }

                curY += cardHeight + cardGap;
            }

            // Execute deferral modifications
            if (deleteIndex >= 0)
            {
                settings.endpoints.RemoveAt(deleteIndex);
                SyncPriorities(settings.endpoints);
            }
            else if (moveUpIndex > 0)
            {
                var temp = settings.endpoints[moveUpIndex];
                settings.endpoints[moveUpIndex] = settings.endpoints[moveUpIndex - 1];
                settings.endpoints[moveUpIndex - 1] = temp;
                SyncPriorities(settings.endpoints);
            }
            else if (moveDownIndex >= 0 && moveDownIndex < settings.endpoints.Count - 1)
            {
                var temp = settings.endpoints[moveDownIndex];
                settings.endpoints[moveDownIndex] = settings.endpoints[moveDownIndex + 1];
                settings.endpoints[moveDownIndex + 1] = temp;
                SyncPriorities(settings.endpoints);
            }

            Widgets.EndScrollView();
            listing.End();
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
            foreach (var node in settings.endpoints)
            {
                _probingNodes[node.id] = true;
                Task.Run(async () =>
                {
                    try
                    {
                        await EndpointHealthProbe.ProbeEndpointAsync(node);
                    }
                    finally
                    {
                        _probingNodes.TryRemove(node.id, out _);
                    }
                });
            }
        }
    }
}
