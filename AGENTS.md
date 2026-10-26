# AGENTS.md — RimMind-Extension-ModelService

RimMind 的独立模型服务扩展模组，提供第三方模型协议适配、本地订阅代理服务（如 Codex / Sub2API）接入网关、多端点负载均衡与高可用自动故障转移。

## 项目定位

Core 本身内置了标准 OpenAI 协议客户端。本扩展模组作为可插拔的扩展层，为 RimMind 运行体系扩展了：
- **原生 Anthropic Claude 协议适配**：支持顶层 `system` 隔离与 `tool_use` / `tool_result` 块互转。
- **本地订阅安全网关（Codex / Sub2API / NewAPI）**：安全强制绑定本机回环地址（`127.0.0.1` / `localhost` / `::1`），杜绝公网泄露本地订阅凭据风险。
- **多端点负载均衡与高可用容灾**：支持按节点权重（`Math.Max(1, weight)`）分配的加权轮询（Weighted Round Robin）与主备故障转移（Priority Failover）；支持单节点独立超时（`node.timeoutSeconds`）与基于失败发生时刻（failure-time clock）的 60 秒自动熔断隔离及半开恢复探测。
- **实时网络延迟与健康探针**：异步测量端点 RTT 延迟与连通状态，区分 HTTP 401/403 未授权响应（记录延迟但标记 `isHealthy = false` 且不重置失败计数），并支持在游戏内设置界面与 DebugAction 一键 Ping 诊断。
- **非持久化环境变量凭据覆盖（`EnvironmentEndpointOverlay`）**：将 `RIMMIND_TEST_*` / `RIMMIND_BACKUP_*` 自动化测试凭据以内存级 shadow 副本方式覆盖到无 Key 的 `OpenCodeGo` 节点，绝不污染或落盘修改玩家持久化设置。

依赖 Core，通过 Core 的 `IAIClientFactory` 扩展接口向 `RimMindAPI.Request` 注册 `extended_service` 与 `opencode_go` 提供商。

## 构建与测试

| 项 | 值 |
|----|-----|
| Target | net48 (源码), net10.0 (单元测试), C# 9.0, Nullable enable |
| Output | `../1.6/Assemblies/` |
| Assembly | RimMindModelService, RootNS: RimMind.ModelService |
| 依赖 | Krafs.Rimworld.Ref, Lib.Harmony.Ref, Newtonsoft.Json, RimMindCore (Domain/Application/Core) |

```powershell
# 编译模组
dotnet build RimMind-Extension-ModelService/Source/RimMindModelService.csproj -c Release

# 运行单测
dotnet test RimMind-Extension-ModelService/Tests/RimMindModelService.Tests.csproj -c Release
```

## 源码结构

```text
Source/
├── RimMindModelServiceMod.cs              Mod 入口，注册 opencode 与 extended_service 提供商工厂及设置页签
├── Balancing/
│   └── LoadBalancer.cs                   多端点调度（Priority Failover / 加权 Round Robin）与熔断隔离恢复
├── Client/
│   ├── EnvironmentEndpointOverlay.cs     自动化环境变量非持久化 shadow 凭据覆盖层（不污染 ModSettings）
│   ├── ModelServiceClient.cs             IAIClient 多端点客户端（单节点超时、失败时刻熔断、永久/瞬态错误分流与密钥脱敏）
│   ├── ModelServiceClientFactory.cs      extended_service 提供商工厂（受 enableService 门控 + EnvironmentEndpointOverlay）
│   └── OpenCodeGoClientFactory.cs        opencode 订阅直连提供商工厂（自动注入 x-opencode-session）
├── Debug/
│   └── ModelServiceDebugActions.cs       DebugAction 批量端点探活诊断（GetEndpointsSnapshot 快照 + MainThread.Post 回调）
├── Diagnostics/
│   └── EndpointHealthProbe.cs            异步 HTTP 连通与 RTT 延迟探针（区分 401/403 未授权与 5xx/网络异常）
├── Integration/
│   └── ModelServiceSettingsTab.cs        ISettingsTab 扩展设置页签注册
├── Models/
│   ├── ModelEndpointConfig.cs            端点配置模型、Scribe 持久化与原子运行期健康/熔断状态
│   ├── ModelEndpointPresets.cs           Codex 本地网关与 OpenCode Go 默认节点预设工厂
│   └── ProviderType.cs                   协议类型枚举与 BalancingStrategy 负载均衡策略枚举
├── Protocol/
│   ├── AnthropicProtocolAdapter.cs       Anthropic Claude 原生 messages 协议与 ephemeral 缓存标记适配
│   └── OpenAIProtocolAdapter.cs          OpenAI 兼容报文与 ToolCall 互转适配器
├── Security/
│   └── LocalLoopbackValidator.cs         本机回环安全检查与公网泄露拦截
└── Settings/
    ├── ModelServiceSettings.cs           Scribe 存档配置、EndpointSyncRoot 锁对象与 GetEndpointsSnapshot 快照
    └── ModelServiceSettingsDrawer.cs     Mod 设置界面（含悬浮 Tooltip、线程安全快照与一键 Ping 诊断）
```

## 关键安全与并发契约

- **回环限制契约**：类型为 `LocalSubscriptionGateway` 的端点，其主机名必须被 `LocalLoopbackValidator.IsLoopbackAddress` 认定为回环地址（`127.0.0.1` / `localhost` / `::1`）。任何指向非回环公网地址的本地订阅请求将被强制记录失败并阻断（若有健康备用节点则故障转移，否则返回 `RimMindErrors.ClientPermanent`），保护玩家的本地代理凭据。
- **环境变量非持久化覆盖契约（`EnvironmentEndpointOverlay`）**：自动化测试环境变量（`RIMMIND_TEST_*` / `RIMMIND_BACKUP_*`）仅通过 `EnvironmentEndpointOverlay.Apply` 生成按节点 ID 缓存的内存 shadow 副本，严禁直接修改 `ModelServiceSettings.endpoints` 中的持久化实例，杜绝测试 API Key 明文写入设置 XML 或覆盖玩家 URL/模型。
- **并发、锁与列表实例契约**：`ModelServiceClientFactory` 尊重 `ModelServiceSettings.enableService` 主开关；`ModelServiceSettings.EndpointSyncRoot` 与 `endpoints` 指向同一列表实例，`EnsureDefaultEndpoints()` 与 `ResetToDefault()` 必须在 `lock (EndpointSyncRoot)` 下原地修改集合（`Clear()` + `AddRange(...)`），保留 `endpoints` 列表引用不变。UI 渲染、批量探活与后台线程读取统一通过 `settings.GetEndpointsSnapshot()` 或 `lock (settings.EndpointSyncRoot)` 获取快照。
- **负载均衡、单节点超时与熔断契约**：`LoadBalancer` 支持主备优先（`PriorityFailover`）与按 `Math.Max(1, weight)` 比例分配的加权轮询（`RoundRobin`）。`ModelServiceClient` 为每个候选节点应用独立的 `node.timeoutSeconds` 超时（最小 5 秒）并叠加总超时；仅对超时、网络异常及 HTTP 408/429/5xx 等瞬态失败在**失败发生时刻**（failure-time timestamp）调用 `RecordFailure` 累加连续失败并触发 60 秒熔断隔离，而 HTTP 400/401/403 等永久客户端错误立即返回且不污染节点健康状态；错误消息中的 Bearer/API Key 经正则脱敏。
- **健康探针鉴权契约（`EndpointHealthProbe`）**：探针收到 HTTP 401/403 时记录实测 RTT 延迟，但必须标记 `isHealthy = false`（返回 `reachable but unauthorized`）且不重置 `consecutiveFailures`；仅 2xx 或 404 视为探活成功并调用 `RecordSuccess`。
- **伪本地化防御契约**：所有动态或外部翻译 Key 调用前必须经 `Translator.CanTranslate` 守卫并提供英文退化文本。
- **Core 边界契约**：严禁侵入 Core 内部队列或私有单例，仅通过 `IAIClient`、`IAIClientFactory`、`ISettingsTab` 与 `RimMindAPI.MainThread.Post` 公共契约协作。
