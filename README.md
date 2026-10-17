<div align="center">

# RimMind-Extension-ModelService 🌐
### Extended Model Gateways, OpenCode Go Subscription & Anthropic Prompt Caching for RimWorld 1.6

**English** | [简体中文](README_zh.md)

<p>
  <a href="https://rimworldgame.com/"><img src="https://img.shields.io/badge/RimWorld-1.6-brightgreen.svg" alt="RimWorld 1.6"></a>
  <a href="https://github.com/mcocdaa/RimWorld-RimMind-Mod-Core"><img src="https://img.shields.io/badge/Dependency-RimMind--Core-blue.svg" alt="Dependency: RimMind-Core"></a>
  <a href="#"><img src="https://img.shields.io/badge/Unit%20Tests-17%2B%20Passing-success.svg" alt="Unit Tests"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License: MIT"></a>
</p>

<p><em>Enterprise multi-provider connectivity: OpenCode Go subscription, Anthropic ephemeral KV caching, local loopback gateways, and failover load balancing.</em></p>

</div>

---

## 📖 Overview

**RimMind-Extension-ModelService** extends `RimMind-Core` with advanced cloud subscription gateways, local loopback proxy security, cutting-edge caching protocols, and zero-downtime failover load balancing.

### Key Highlights
- **OpenCode Go Subscription Direct Access**: One-click subscription preset (`https://opencode.ai/zen/go/v1`) via `OpenCodeGoClientFactory` with automatic session isolation headers (`x-opencode-session`).
- **Anthropic Protocol Adapter**: Implements native Anthropic `messages` protocol with `cache_control: {"type": "ephemeral"}` markings on static L0/L1 prefix layers, maximizing hardware-level KV cache reuse.
- **Local Subscription Loopback Security**: Strictly enforces loopback binding (`127.0.0.1`, `localhost`, `::1`) via `LocalLoopbackValidator` for local subscription gateways (Codex, Sub2API, NewAPI), preventing accidental credential leakage over public networks.
- **Multi-Endpoint Load Balancing & Failover**: Supports `PriorityFailover` and `RoundRobin` routing with 60-second circuit isolation (`LoadBalancer`), millisecond HTTP latency probing (`EndpointHealthProbe`), and F8 Debug Hub diagnostics (`ModelServiceF8Diagnostics`).

---

## 🎮 In-Game Showcase

![RimMind-Extension-ModelService Showcase](docs/images/showcase.jpg)
*High-speed gateway settings: Testing real-time connection latency with OpenCode Go and configuring automatic endpoint failover.*

---

## 🏛️ Gateway & Protocol Architecture

```mermaid
flowchart TD
    Core["RimMind-Core Request Dispatcher"] --> Factory["IAIClientFactory Registry"]
    Factory --> OpenCode["OpenCodeGoClientFactory (x-opencode-session)"]
    Factory --> Extended["ModelServiceClientFactory (enableService Gated)"]
    Extended --> Balancer["LoadBalancer (PriorityFailover / RoundRobin)"]
    Balancer --> OpenAI["OpenAIProtocolAdapter"]
    Balancer --> Anthropic["AnthropicProtocolAdapter (Ephemeral KV Cache)"]
    Balancer --> Loopback["LocalLoopbackValidator (127.0.0.1 / ::1 Guard)"]
    Balancer --> Health["EndpointHealthProbe & F8 Diagnostics"]
```

---

## 🛠️ Installation & Load Order

```text
1. Harmony
2. Core (Vanilla RimWorld)
3. RimMind-Core
4. RimMind-Extension-ModelService
```

---

## ⚙️ Settings Reference

Configurable via **Options -> Mod Settings -> RimMind - Extended Model Service**:

| Setting | Default | Description |
|---|---|---|
| **Enable Extended Service** | `Enabled` | Master switch gating `extended_service` multi-endpoint routing |
| **Balancing Strategy** | `PriorityFailover` | Route by priority order (`PriorityFailover`) or distribute evenly (`RoundRobin`) |
| **Provider Type** | `OpenAICompatible` | Protocol adapter (`OpenAICompatible`, `AnthropicClaude`, `LocalSubscriptionGateway`, `OpenCodeGo`) |
| **Endpoint Priority & Enabled** | `1 / Enabled` | Per-endpoint priority weight and active participation toggle |
| **One-Click Latency Ping** | — | Asynchronous HTTP health probe measuring RTT latency and updating endpoint health |

---

## 🧪 Developer Guide & Testing

Run unit tests and build from the repository root:

```powershell
dotnet build RimMind-Extension-ModelService/Source/RimMindModelService.csproj -c Release
dotnet test RimMind-Extension-ModelService/Tests/RimMindModelService.Tests.csproj -c Release
```

---

## 📜 License

Licensed under the [MIT License](LICENSE).
