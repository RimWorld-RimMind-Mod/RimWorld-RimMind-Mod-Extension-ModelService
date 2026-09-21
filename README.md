# RimMind-Extension-ModelService

> RimMind 独立模型服务扩展模组（Model Provider Extension Mod）

本模组为 RimMind 提供了强大的外部模型服务扩展能力，特别针对现代大模型多样化接入场景进行了深度定制：

## 功能特性

1. **多协议适配**
   - **OpenAI 兼容协议**：对接 OpenAI 官方 API、各类第三方中转 API 及开源本地推理后端（如 Ollama、vLLM、LM Studio、LocalAI）。
   - **Anthropic Claude 协议**：原生 Claude Messages API 协议支持，支持工具调用（Tool Use）与 System Prompt 分离。

2. **本地订阅安全接入（Codex / Sub2API）**
   - 专门支持本地订阅转 API 服务（如借助各类本地网关将个人订阅转化为可供 Mod 调用的 API 接口）。
   - **严格回环安全隔离**：强制校验端点主机地址（仅允许 `127.0.0.1`、`localhost`、`::1`），防止意外将本地敏感凭证发送到公网第三方。

3. **智能容灾与高可用负载均衡**
   - **主备故障转移（Priority Failover）**：优先使用主端点，当主端点因配额耗尽、网络抖动或服务不可用时，毫秒级自动切换到备用端点；
   - **多端点轮询（Round Robin）**：在多个可用的等价端点间轮流分摊请求负载；
   - **健康熔断机制**：连续失败端点自动进入 60 秒隔离熔断期，熔断后自动尝试恢复探测，杜绝死锁与无效阻塞。

4. **实时延迟与健康探针**
   - 在 Mod 设置界面中提供一键异步 Ping 测速，直观展示各端点往返时间（RTT）与连接状态（OK / 错误信息）。

## 安装与依赖

- 依赖 `RimMind-Core`（以及 `Lib.Harmony`）。
- 在 RimWorld 模组加载顺序中置于 `RimMind-Core` 之后即可。
