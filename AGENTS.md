# AGENTS.md — RimMind-Extension-ModelService

RimMind 的独立模型服务扩展模组，提供第三方模型协议适配、本地订阅代理服务（如 Codex / Sub2API）接入网关、多端点负载均衡与高可用自动故障转移。

## 项目定位

Core 本身内置了标准 OpenAI 协议客户端。本扩展模组作为可插拔的扩展层，为 RimMind 运行体系扩展了：
- **原生 Anthropic Claude 协议适配**：支持顶层 `system` 隔离与 `tool_use` / `tool_result` 块互转。
- **本地订阅安全网关（Codex / Sub2API / NewAPI）**：安全强制绑定本机回环地址（`127.0.0.1` / `localhost` / `::1`），杜绝公网泄露本地订阅凭据风险。
- **多端点负载均衡与高可用容灾**：支持轮询（Round Robin）与主备故障转移（Priority Failover），并具备 60 秒自动熔断与恢复探测机制。
- **实时网络延迟与健康探针**：异步测量端点 RTT 延迟与连通状态，并支持在游戏内设置界面一键 Ping 诊断。

依赖 Core，通过 Core 的 `IAIClientFactory` 扩展接口向 `RimMindAPI.Request` 注册 `extended_service` 提供商。

## 构建与测试

| 项 | 值 |
|----|-----|
| Target | net48 (源码), net10.0 (单元测试), C# 9.0, Nullable enable |
| Output | `../1.6/Assemblies/` |
| Assembly | RimMindModelService, RootNS: RimMind.ModelService |
| 依赖 | Krafs.Rimworld.Ref, Lib.Harmony.Ref, Newtonsoft.Json, RimMindCore (Domain/Application) |

```powershell
# 编译模组
dotnet build RimMind-Extension-ModelService/Source/RimMindModelService.csproj -c Release

# 运行单测
dotnet test RimMind-Extension-ModelService/Tests/RimMindModelService.Tests.csproj -c Release
```

## 源码结构

```
Source/
├── RimMindModelServiceMod.cs              Mod 入口，注册提供商工厂
├── Models/
│   ├── ProviderType.cs                   提供商枚举（OpenAI, Claude, 本地订阅网关）
│   ├── BalancingStrategy.cs              负载均衡策略枚举（主备优先, 轮询）
│   └── ModelEndpointConfig.cs            端点配置模型与持久化
├── Security/
│   └── LocalLoopbackValidator.cs         本机回环安全检查与公网拦截
├── Protocol/
│   ├── OpenAIProtocolAdapter.cs          OpenAI 报文互转适配器
│   └── AnthropicProtocolAdapter.cs       Anthropic Claude 报文互转适配器
├── Balancing/
│   └── LoadBalancer.cs                   多端点调度与熔断隔离
├── Diagnostics/
│   └── EndpointHealthProbe.cs            异步 HTTP 连通与延迟探针
├── Client/
│   ├── ModelServiceClient.cs             IAIClient 综合客户端实现
│   └── ModelServiceClientFactory.cs      IAIClientFactory 工厂
└── Settings/
    ├── ModelServiceSettings.cs           Scribe 存档配置
    └── ModelServiceSettingsDrawer.cs     Mod 设置界面与 Ping 诊断绘制
```

## 关键安全契约

- **回环限制契约**：类型为 `LocalSubscriptionGateway` 的端点，其主机名必须被 `LocalLoopbackValidator.IsLoopbackAddress` 认定为回环地址。任何指向非回环公网地址的本地订阅请求将被强制阻断并抛出 `RimMindErrors.ClientPermanent`，保护玩家的 Codex/本地代理凭据。
- **Core 边界契约**：严禁侵入 Core 内部队列或私有单例，仅通过 `IAIClient` 与 `IAIClientFactory` 公共契约协作。
