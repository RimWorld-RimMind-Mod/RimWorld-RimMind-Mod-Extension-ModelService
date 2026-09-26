<div align="center">

# RimMind-Extension-ModelService 🌐
### Extended Model Gateways, OpenCode Go Subscription & Anthropic Prompt Caching for RimWorld 1.6

**English** | [简体中文](README_zh.md)

<p>
  <a href="https://rimworldgame.com/"><img src="https://img.shields.io/badge/RimWorld-1.6-brightgreen.svg" alt="RimWorld 1.6"></a>
  <a href="https://github.com/mcocdaa/RimWorld-RimMind-Mod-Core"><img src="https://img.shields.io/badge/Dependency-RimMind--Core-blue.svg" alt="Dependency: RimMind-Core"></a>
  <a href="#"><img src="https://img.shields.io/badge/Unit%20Tests-14%2B%20Passing-success.svg" alt="Unit Tests"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License: MIT"></a>
</p>

<p><em>Enterprise multi-provider connectivity: OpenCode Go subscription, Anthropic ephemeral KV caching, and failover load balancing.</em></p>

</div>

---

## 📖 Overview

**RimMind-Extension-ModelService** extends `RimMind-Core` with advanced cloud subscription gateways, cutting-edge caching protocols, and zero-downtime failover load balancing.

### Key Highlights
- **OpenCode Go Subscription Direct Access**: One-click subscription preset (`https://opencode.ai/zen/go/v1`) with automatic session isolation headers (`x-opencode-session`).
- **Anthropic Protocol Adapter**: Implements native Anthropic `messages` protocol with `cache_control: {"type": "ephemeral"}` markings on static L0/L1 prefix layers, maximizing hardware-level KV cache reuse.
- **Multi-Endpoint Latency Ping & Failover**: Monitors endpoint health with millisecond ping checks and transparently shifts requests to backup providers if a primary endpoint returns HTTP 429 / 5xx errors.

---

## 🎮 In-Game Showcase

![RimMind-Extension-ModelService Showcase](docs/images/showcase.jpg)
*High-speed gateway settings: Testing real-time connection latency with OpenCode Go and configuring automatic endpoint failover.*

---

## 🏛️ Gateway & Protocol Architecture

```mermaid
flowchart TD
    Core["RimMind-Core Request Dispatcher"] --> Factory["IAIClientFactory Extension"]
    Factory --> OpenCode["OpenCodeGoClient (Session Headers)"]
    Factory --> Anthropic["AnthropicProtocolAdapter (Ephemeral Cache)"]
    Factory --> Failover["Failover Load Balancer"]
    Failover --> Health["Periodic Health & Latency Ping Monitor"]
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

## 🧪 Developer Guide & Testing

Run unit tests directly:

```powershell
dotnet test RimMind-Extension-ModelService/Tests/RimMindModelService.Tests.csproj -c Release
```

---

## 📜 License

Licensed under the [MIT License](LICENSE).
