# Core API

[Wiki 首页](../README.md) · [Extensibility](../extensibility/README.md) · [Runtime](../runtime/README.md)

Core 只提供不依赖 Assets、Scene、Rendering、Editor、Build 或具体 Native backend 的基础能力。

| 项目 | 职责 |
| --- | --- |
| [Inno.Core.Diagnostics](Inno.Core.Diagnostics.md) | 当前诊断状态与失败 sink 隔离 |
| [Inno.Core.Logging](Inno.Core.Logging.md) | host-owned 异步日志路由与 Session identity |
| [Inno.Core.Mathematics](Inno.Core.Mathematics.md) | 向量、矩阵、四元数、颜色与矩形 |
| [Inno.Core.Identity](Inno.Core.Identity.md) | runtime/persistent identity |
| [Inno.Core.Serialization](Inno.Core.Serialization.md) | 当前格式结构化序列化与 generation lease |
| [Inno.Core.Serialization.Generators](Inno.Core.Serialization.Generators.md) | 普通 DTO converter source generation |
| [Inno.Core.Storage](Inno.Core.Storage.md) | 索引对象存储与依赖图 |
| [Inno.Core.Events](Inno.Core.Events.md) | 有序同步/排队事件分发 |
| [Inno.Core.IO](Inno.Core.IO.md) | 原子文件/目录提交与路径边界 |
| [Inno.Core.Input](Inno.Core.Input.md) | 后端中立输入枚举 |
| [Inno.Core.Jobs](Inno.Core.Jobs.md) | 单线程/工作窃取 JobScheduler |
| [Inno.Core.Coroutines](Inno.Core.Coroutines.md) | session-owned 协程调度 |
| [Inno.Core.Graphs](Inno.Core.Graphs.md) | 通用图文档、验证与 codec |
| [Inno.Core.Settings](Inno.Core.Settings.md) | Project Settings 与贡献组合 |

Core 不再拥有 Shell、静态 Manager、程序集加载或脚本编译；这些职责分别属于 Runtime、Extensibility 和 Scripting。
