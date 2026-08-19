# Inno.Editor.Diagnostics

[Editor 索引](README.md) · [Core Logging](../core/Inno.Core.Logging.md) · [Application](Inno.Editor.Application.md)

`Inno.Editor.Diagnostics` 拥有 Editor 的 Logging 与 Stats 功能。目录使用 `Logging/`，避免名为 `Log/` 的目录在工具、ignore 规则或导出流程中与日志输出目录混淆。

## Public API

| API | 说明 |
| --- | --- |
| `DiagnosticsModule` | 自动注册和移除 Editor diagnostic sinks。 |
| `logs` | 当前 `EditorLogBuffer`。 |
| Module lifecycle | 由 `EditorRuntime` 自动调用，无手工注册或 context extension。 |
| `EditorLogBuffer` | 有容量限制的 thread-safe log sink；支持 `capacity`、`Receive`、`Snapshot`、`Clear`。 |
| `LogPanel` | 过滤、collapse、展开详情和 bottom-follow。 |
| `StatsPanel` | 显示平滑采样后的 frame delta 与 FPS。 |

LogPanel 只在用户当前位于底部时跟随新日志；查看历史位置不会被强制拉回。严重级颜色、卡片背景和 hover 状态全部来自 `EditorPalette`。

Stats 使用时间窗口平均值，不直接显示单帧抖动，因此 FPS 与 delta 更接近游戏内常见统计显示。

## Scripting boundary

Diagnostics 的 buffer 与 sink lifecycle 是 Host 能力，目前不导出 EditorScripts facade。`LogPanel` 通过构造函数直接注入 `DiagnosticsModule`。游戏或工具脚本写日志使用 `InnoEngine.Logging.Log`；Editor Diagnostics 自动消费同一个 `LogManager` stream。
