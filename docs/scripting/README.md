# Scripting API

[Wiki 首页](../README.md) · [Extensibility](../extensibility/README.md) · [Editor](../editor/README.md)

| 项目 | 职责 |
| --- | --- |
| [Inno.Scripting.Api](Inno.Scripting.Api.md) | 显式 API export、logical namespace 与 attachable type attribute |
| [Inno.Scripting.Compiler](Inno.Scripting.Compiler.md) | Roslyn compilation、裁剪引用、诊断、cache 与 runtime/editor artifact |
| [Inno.Scripting.Reload](Inno.Scripting.Reload.md) | candidate assembly activation、coordinator 与 last-good generation |

Play、Build 与 Editor workflow 共用同一编译契约。Compilation Ticket 绑定 source/reference/plugin generation；旧结果永不激活。
