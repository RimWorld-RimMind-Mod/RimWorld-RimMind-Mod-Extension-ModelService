using System;
using System.Diagnostics;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces;
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
            if (settings == null || settings.endpoints.Count == 0)
            {
                Messages.Message("[ModelService] 未配置任何端点，请在设置中添加预设节点。", MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            Messages.Message($"[ModelService] 开始探测全部 {settings.endpoints.Count} 个模型服务节点...", MessageTypeDefOf.NeutralEvent, false);

            foreach (var node in settings.endpoints)
            {
                var targetNode = node;
                Task.Run(async () =>
                {
                    var (isOk, rttMs, error) = await EndpointHealthProbe.ProbeEndpointAsync(targetNode);

                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        if (isOk)
                        {
                            Messages.Message($"[ModelService] 🟢 {targetNode.name} ({targetNode.modelName}): 延迟 {rttMs}ms (连接成功)", MessageTypeDefOf.PositiveEvent, false);
                        }
                        else
                        {
                            Messages.Message($"[ModelService] 🔴 {targetNode.name}: 失败 - {error}", MessageTypeDefOf.NegativeEvent, false);
                        }
                    });
                });
            }
        }
    }
}

