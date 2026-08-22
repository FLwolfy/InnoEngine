# Inno.Editor.Panel.Logging

[Editor 索引](README.md) · [Core Logging](../core/Inno.Core.Logging.md) · [Core Diagnose](../core/Inno.Core.Diagnose.md) · [Stats](Inno.Editor.Panel.Stats.md)

该项目同时订阅彼此独立的 `Inno.Core.Logging` 与 `Inno.Core.Diagnose`，并把两种数据组合成统一的 Editor Console。目录和项目名使用 `Logging`，避免 `Log` 被 ignore 规则或输出目录约定误判。

## 组成

- `LoggingModule`：随 Editor runtime 启停普通日志和当前诊断订阅。
- `EditorLogBuffer`：保留有界、追加式日志历史。
- `EditorDiagnosticBuffer`：按 producer 替换当前诊断，不把过期编译结果写入历史。
- `ConsolePanel`：统一排序两种流，并提供来源标识、等级过滤、折叠和详情显示。

当滚动条原本位于底部时，新日志会继续滚到底部；用户向上浏览后不会抢夺滚动位置。按钮、折叠三角、行距与颜色全部来自 `ImGuiWidget`、`EditorStyleMetrics` 和 `EditorPalette`。

游戏脚本的 `InnoEngine.Logging.Log` 与 Editor Panel 消费的是同一 Core stream。生命周期日志只会在 Scene 真正执行 Runtime lifecycle 时产生；Edit Mode 中单纯切换 enabled 不会伪造 `OnEnable`/`OnDisable`。

脚本编译使用 `Script Compiler` group，每次编译完成都通过 `Diagnostics.Set` 设置完整结果；下一次干净编译设置空集合后，旧 warning/error 自动消失。脚本 reload 使用独立的 `Script Reload` group，因此编译结果与状态迁移问题不会互相覆盖。普通 Log 仍一直保留到容量淘汰或用户点击 Clear。

Console 还会实时显示 Asset Import/Build/Catalog、Asset Source Database、Scene Workspace、Editor Workspace、Panel Activation 和 Project Persistence 的当前报告。Diagnostic 恢复时对应卡片自动消失；同一失败首次出现时写入的异常 Log 不会随之删除。这使 Console 同时保留“现在需要处理什么”和“过去发生过什么”，但两者不会混成一条不可清理的历史流。

Console card header 只显示等级，例如 `[Info]` 或 `[Error]`。展开后的详情通过 `Kind: Log` 或 `Kind: Diagnostic` 明确来源，`Copy Full Entry` 文本也保留同一来源信息，避免等级相同的历史日志与当前诊断产生歧义。所有 card 都使用统一 Editor Action/Menu 系统提供右键菜单：

- `Copy Message`：复制诊断 code 与消息正文。
- `Copy Full Entry`：复制时间、等级、category、重复次数和源文件位置。

菜单复用全局 `EditorMenuRenderer` 与 Context Menu 样式，因此会正确捕获 hover/input，不把右键事件传递给后方 Panel 内容。

该 Panel 当前没有单独的 EditorScripts facade；日志写入 API 由 Core Logging 的 Runtime scripting profile 导出。
