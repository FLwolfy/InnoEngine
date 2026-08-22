# Inno.Editor.Panel.Logging

[Editor 索引](README.md) · [Core Logging](../core/Inno.Core.Logging.md) · [Core Diagnostics](../core/Inno.Core.Diagnostics.md) · [Stats](Inno.Editor.Panel.Stats.md)

该项目同时订阅彼此独立的 `Inno.Core.Logging` 与 `Inno.Core.Diagnostics`，并把两种数据组合成统一的 Editor Console。目录和项目名使用 `Logging`，避免 `Log` 被 ignore 规则或输出目录约定误判。

## 组成

- `LoggingModule`：随 Editor runtime 启停普通日志和当前诊断订阅。
- `EditorLogBuffer`：保留有界、追加式日志历史。
- `EditorDiagnosticBuffer`：按 producer 替换当前诊断，不把过期编译结果写入历史。
- `LogPanel`：统一排序两种流，并提供等级过滤、折叠和详情显示。

当滚动条原本位于底部时，新日志会继续滚到底部；用户向上浏览后不会抢夺滚动位置。按钮、折叠三角、行距与颜色全部来自 `ImGuiWidget`、`EditorStyleMetrics` 和 `EditorPalette`。

游戏脚本的 `InnoEngine.Logging.Log` 与 Editor Panel 消费的是同一 Core stream。生命周期日志只会在 Scene 真正执行 Runtime lifecycle 时产生；Edit Mode 中单纯切换 enabled 不会伪造 `OnEnable`/`OnDisable`。

脚本编译使用 `editor.scripting.compiler` 诊断 source。每次编译完成都会显式发布完整结果：失败或带 warning 时显示当前结果；下一次干净编译发布空集合后，旧 warning/error 自动消失。脚本 reload 使用独立的 `editor.scripting.reload` source，因此编译结果与状态迁移问题不会互相覆盖。普通 Log 仍一直保留到容量淘汰或用户点击 Clear。

每个折叠或展开的 Console card 都使用统一 Editor Action/Menu 系统提供右键菜单：

- `Copy Message`：复制诊断 code 与消息正文。
- `Copy Full Entry`：复制时间、等级、category、重复次数和源文件位置。

菜单复用全局 `EditorMenuRenderer` 与 Context Menu 样式，因此会正确捕获 hover/input，不把右键事件传递给后方 Panel 内容。

该 Panel 当前没有单独的 EditorScripts facade；日志写入 API 由 Core Logging 的 Runtime scripting profile 导出。
