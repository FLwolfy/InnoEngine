# Inno.Editor.Panel.Logging

[Editor 索引](README.md) · [Core Logging](../core/Inno.Core.Logging.md) · [Stats](Inno.Editor.Panel.Stats.md)

该项目将 `Inno.Core.Logging` 的日志事件缓冲为 Editor 可浏览的数据，并提供独立 Log Panel。目录和项目名使用 `Logging`，避免 `Log` 被 ignore 规则或输出目录约定误判。

## 组成

- `LoggingModule`：随 Editor runtime 启停日志订阅。
- `EditorLogBuffer`：有界、按版本更新的日志快照。
- `LogPanel`：级别过滤、搜索、清除、折叠和详情显示。

当滚动条原本位于底部时，新日志会继续滚到底部；用户向上浏览后不会抢夺滚动位置。按钮、折叠三角、行距与颜色全部来自 `ImGuiWidget`、`EditorStyleMetrics` 和 `EditorPalette`。

游戏脚本的 `InnoEngine.Logging.Log` 与 Editor Panel 消费的是同一 Core stream。生命周期日志只会在 Scene 真正执行 Runtime lifecycle 时产生；Edit Mode 中单纯切换 enabled 不会伪造 `OnEnable`/`OnDisable`。

该 Panel 当前没有单独的 EditorScripts facade；日志写入 API 由 Core Logging 的 Runtime scripting profile 导出。
