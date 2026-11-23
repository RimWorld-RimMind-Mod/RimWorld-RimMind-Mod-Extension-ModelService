using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LudeonTK;
using RimMind.ModelService.Diagnostics;
using RimMind.ModelService.Models;
using RimMind.Presentation.Api;
using RimWorld;
using Verse;

namespace RimMind.ModelService.Debug
{
    public static class ModelServiceDebugActions
    {
        [DebugAction("RimMind - ModelService", "Test All Enabled Endpoints", actionType = DebugActionType.Action)]
        public static void TestAllEndpoints()
        {
            var settings = RimMindModelServiceMod.Settings;
            var snapshot = settings?.GetEndpointsSnapshot() ?? Array.Empty<ModelEndpointConfig>();
            var enabledNodes = new List<ModelEndpointConfig>(snapshot.Length);
            for (int i = 0; i < snapshot.Length; i++)
            {
                var ep = snapshot[i];
                if (ep != null && ep.isEnabled)
                {
                    enabledNodes.Add(ep);
                }
            }

            if (enabledNodes.Count == 0)
            {
                Messages.Message(
                    RimMindText.TrOr(
                        "RimMind.ModelService.Debug.NoEndpoints",
                        "[ModelService] No endpoints configured. Please add preset nodes in Mod Settings."),
                    MessageTypeDefOf.NeutralEvent,
                    false);
                return;
            }

            Messages.Message(
                RimMindText.TrOr(
                    "RimMind.ModelService.Debug.ProbingAll",
                    "[ModelService] Probing all {0} model service node(s)...",
                    enabledNodes.Count),
                MessageTypeDefOf.NeutralEvent,
                false);

            foreach (var node in enabledNodes)
            {
                var targetNode = node;
                Task.Run(async () =>
                {
                    var (isOk, rttMs, error) = await EndpointHealthProbe.ProbeEndpointAsync(targetNode);

                    // Task.Run body: Verse calls must be marshalled back to the main thread.
                    RimMindAPI.MainThread.Post(() =>
                    {
                        if (isOk)
                        {
                            Messages.Message(
                                RimMindText.TrOr(
                                    "RimMind.ModelService.Debug.ProbeSuccess",
                                    "[ModelService] {0} ({1}): latency {2}ms (Connected)",
                                    targetNode.name,
                                    targetNode.modelName,
                                    rttMs),
                                MessageTypeDefOf.PositiveEvent,
                                false);
                        }
                        else
                        {
                            Messages.Message(
                                RimMindText.TrOr(
                                    "RimMind.ModelService.Debug.ProbeFailed",
                                    "[ModelService] {0}: Failed - {1}",
                                    targetNode.name,
                                    error),
                                MessageTypeDefOf.NegativeEvent,
                                false);
                        }
                    });
                });
            }
        }
    }
}

