# Inno.Core.Jobs

[Core 索引](README.md) · [Runtime](../runtime/Inno.Runtime.md)

## 公开 API

- `JobScheduler`：session-owned scheduler，支持单线程与工作窃取执行。
- `JobSchedulerOptions`, `JobExecutionMode`：线程和执行策略。
- `JobHandle`：带 generation 的依赖/完成句柄。

一个 scheduler 只能由其 owner 按 BeginFrame/EndFrame contract 驱动。单线程模式拒绝跨线程 mutation；工作窃取模式允许并发 schedule，但 main-thread queue 只在 owner thread drain。stale handle、重复 frame 和 job exception 明确失败。
