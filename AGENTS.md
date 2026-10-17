# AGENTS.md — RimMind-Extension-ModelService

RimMind 的独立模型服务扩展模组，提供第三方模型协议适配、本地订阅代理服务（如 Codex / Sub2API）接入网关、多端点负载均衡与高可用自动故障转移。

## 项目定位

Core 本身内置了标准 OpenAI 协议客户端。本扩展模组作为可插拔的扩展层，为 RimMind 运行体系扩展了：
- **原生 Anthropic Claude 协议适配**：支持顶层 `system` 隔离与 `tool_use` / `tool_result` 块互转。
- **本地订阅安全网关（Codex / Sub2API / NewAPI）**：安全强制绑定本机回环地址（`127.0.0.1` / `localhost` / `::1`），杜绝公网泄露本地订阅凭据风险。
- **多端点负载均衡与高可用容灾**：支持轮询（Round Robin）与主备故障转移（Priority Failover），并具备 60 秒自动熔断与恢复探测机制。
- **实时网络延迟与健康探针**：异步测量端点 RTT 延迟与连通状态，并支持在游戏内设置界面一键 Ping 诊断。

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
├── RimMindModelServiceMod.cs              Mod 入口，注册 extended_service 与 opencode_go 提供商工厂
├── Balancing/
│   └── LoadBalancer.cs                   多端点调度（Priority Failover / Round Robin）与熔断隔离
├── Client/
│   ├── ModelServiceClient.cs             IAIClient 多端点综合客户端实现（线程安全快照读取）
│   ├── ModelServiceClientFactory.cs      extended_service 提供商工厂（受 enableService 开关门控）
│   └── OpenCodeGoClientFactory.cs        opencode_go 订阅直连提供商工厂（自动注入 x-opencode-session）
├── Debug/
│   └── ModelServiceF8Diagnostics.cs      F8 调试中心网关状态与端点健康检视面板
├── Diagnostics/
│   └── EndpointHealthProbe.cs            异步 HTTP 连通与 RTT 延迟探针（5xx/异常自动标记不健康）
├── Integration/
│   └── GatewayCircuitBreakerIntegration.cs 网关级熔断状态同步与故障隔离集成
├── Models/
│   ├── BalancingStrategy.cs              负载均衡策略枚举（主备优先, 轮询）
│   ├── ModelEndpointConfig.cs            端点配置模型与 Scribe 持久化
│   └── ProviderType.cs                   协议类型枚举（OpenAI, Anthropic Claude, 本地订阅网关, OpenCode Go）
├── Protocol/
│   ├── AnthropicProtocolAdapter.cs       Anthropic Claude 原生 messages 协议与 ephemeral 缓存标记适配
│   └── OpenAIProtocolAdapter.cs          OpenAI 兼容报文与 ToolCall 互转适配器
├── Security/
│   └── LocalLoopbackValidator.cs         本机回环安全检查与公网泄露拦截
└── Settings/
    ├── ModelServiceSettings.cs           Scribe 存档配置与端点集合管理
    └── ModelServiceSettingsDrawer.cs     Mod 设置界面（含悬浮 Tooltip 与一键 Ping 诊断）
```

## 关键安全与并发契约

- **回环限制契约**：类型为 `LocalSubscriptionGateway` 的端点，其主机名必须被 `LocalLoopbackValidator.IsLoopbackAddress` 认定为回环地址（`127.0.0.1` / `localhost` / `::1`）。任何指向非回环公网地址的本地订阅请求将被强制阻断并抛出 `RimMindErrors.ClientPermanent`，保护玩家的本地代理凭据。
- **并发与开关契约**：`ModelServiceClientFactory` 尊重 `ModelServiceSettings.enableService` 主开关；后台线程访问 `endpoints` 列表统一在同一 `List<ModelEndpointConfig>` 实例上加锁快照，防止 UI 编辑与后台请求并发竞争。
- **伪本地化防御契约**：所有动态或外部翻译 Key 调用前必须经 `Translator.CanTranslate` 守卫并提供英文退化文本。
- **Core 边界契约**：严禁侵入 Core 内部队列或私有单例，仅通过 `IAIClient` 与 `IAIClientFactory` 公共契约协作。
