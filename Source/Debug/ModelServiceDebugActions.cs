using System;
using System.Diagnostics;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces;
using RimMind.Presentation.Api;
using RimMind.ModelService.Diagnostics;
using RimMind.ModelService.Models;
using RimMind.ModelService.Settings;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.ModelService.Debug
{
    public static class ModelServiceDebugActions
    {
        [DebugAction("RimMind - ModelService", "Test All Endpoints (OpenCode Go / Codex)", actionType = DebugActionType.Action)]
        public static void TestAllEndpoints()
        {
            var settings = RimMindModelServiceMod.Settings;
            var snapshot = settings?.GetEndpointsSnapshot() ?? Array.Empty<ModelEndpointConfig>();
            if (snapshot.Length == 0)
            {
                Messages.Message("RimMind.ModelService.Debug.NoEndpoints".Translate(), MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            Messages.Message("RimMind.ModelService.Debug.ProbingAll".Translate(snapshot.Length), MessageTypeDefOf.NeutralEvent, false);

            foreach (var node in snapshot)
            {
                var targetNode = node;
                if (targetNode == null) continue;
                Task.Run(async () =>
                {
                    var (isOk, rttMs, error) = await EndpointHealthProbe.ProbeEndpointAsync(targetNode);

                    // Task.Run body: Verse calls must be marshalled back to the main thread.
                    RimMindAPI.MainThread.Post(() =>
                    {
                        if (isOk)
                        {
                            Messages.Message("RimMind.ModelService.Debug.ProbeSuccess".Translate(targetNode.name, targetNode.modelName, rttMs), MessageTypeDefOf.PositiveEvent, false);
                        }
                        else
                        {
                            Messages.Message("RimMind.ModelService.Debug.ProbeFailed".Translate(targetNode.name, error), MessageTypeDefOf.NegativeEvent, false);
                        }
                    });
                });
            }
        }
    }
}

