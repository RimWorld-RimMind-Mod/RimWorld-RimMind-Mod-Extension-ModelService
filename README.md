# RimMind-Extension-ModelService

> RimMind 独立模型服务扩展模组（Model Provider Extension Mod）

本模组为 RimMind 提供了强大的外部模型服务扩展能力，特别针对现代大模型多样化接入场景进行了深度定制：

## 功能特性

1. **多协议适配与 Prompt Caching (KV-Cache) 优化**
   - **OpenAI 兼容协议**：对接 OpenAI 官方 API、DeepSeek、各类第三方中转 API 及开源本地推理后端（如 Ollama、vLLM、LM Studio、LocalAI）。
   - **Anthropic Claude 协议与前缀缓存**：原生 Claude Messages API 协议支持，系统提示词严格提取 L0/L1 静态前缀，易变观察尾部注入 `<observation>`，工具声明字典序排序并自动注入 `cache_control: {"type": "ephemeral"}` 标记，充分释放 Claude Prompt Caching 性能红利。

2. **OpenCode Go 订阅直连**
   - 原生内置 OpenCode Go 订阅端点工厂（`https://opencode.ai/zen/go/v1`），支持 `deepseek-v4.1-flash` 等高速模型。
   - 自动注入 `x-opencode-session` 隔离头与独立安全凭证管理。

3. **动态服务商注入与零侵入解耦**
   - 遵循单向依赖，在模组启动时通过 Core 的公共契约 `RimMindAPI.Ext.RegisterClientFactory(...)` 动态注册端点工厂。
   - Core 保持纯净轻量，无需对任何第三方网关进行硬编码检测或反射。

4. **本地订阅安全接入（Codex / Sub2API）**
   - 专门支持本地订阅转 API 服务（如借助各类本地网关将个人订阅转化为可供 Mod 调用的 API 接口）。
   - **严格回环安全隔离**：强制校验端点主机地址（仅允许 `127.0.0.1`、`localhost`、`::1`），防止意外将本地敏感凭证发送到公网第三方。

5. **智能容灾与高可用负载均衡**
   - **主备故障转移（Priority Failover）**：优先使用主端点，当主端点因配额耗尽、网络抖动或服务不可用时，毫秒级自动切换到备用端点；
   - **多端点轮询（Round Robin）**：在多个可用的等价端点间轮流分摊请求负载；
   - **健康熔断机制**：连续失败端点自动进入 60 秒隔离熔断期，熔断后自动尝试恢复探测，杜绝死锁与无效阻塞。

6. **实时延迟与健康探针**
   - 在 Mod 设置界面中提供一键异步 Ping 测速，直观展示各端点往返时间（RTT）与连接状态（OK / 错误信息）。

## 子模组列表与依赖关系

| 模组 | 职责 | 依赖 | GitHub |
|------|------|------|--------|
| RimMind-Core | 公共 API、LLM 请求调度、4-Zone 上下文引擎、ToolCall 契约与运行时 | Harmony | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Core) |
| RimMind-Actions | 将基础 ToolCall 组合成高级 Mechanism 动作 | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Actions) |
| RimMind-Advisor | 状态/Thought → 建议、审批、动作与反馈闭环 | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Advisor) |
| RimMind-Dialogue | AI 驱动的对话系统与社交关系演进（express_dialogue） | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Dialogue) |
| RimMind-Memory | 三层记忆系统（情景/摘要/反思）与时间上下文 | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Memory) |
| RimMind-Personality | 基于概率的状态跃迁驱动的人格与 Thought 注入 | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Personality) |
| RimMind-Storyteller | AI 叙事者，智能评估戏剧性曲线与事件选择 | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Storyteller) |
| RimMind-Bridge-RimChat | RimMind 与 RimChat 模组的协调与门控互斥 | Core, RimChat | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Bridge-RimChat) |
| RimMind-Bridge-RimTalk | RimMind 与 RimTalk 模组的对话气泡与上下文桥 | Core, RimTalk | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Bridge-RimTalk) |
| **RimMind-Extension-ModelService** | **扩展模型网关、OpenCode Go 订阅直连与多端点负载均衡** | Core | [链接](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Extension-ModelService) |

## 安装与依赖

- 依赖 `RimMind-Core`（以及 `Lib.Harmony`）。
- 在 RimWorld 模组加载顺序中置于 `RimMind-Core` 之后即可。
