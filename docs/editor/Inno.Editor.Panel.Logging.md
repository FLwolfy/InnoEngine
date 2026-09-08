# Inno.Editor.Panel.Logging

[Editor 索引](README.md) · [Core Logging](../core/Inno.Core.Logging.md) · [Core Diagnose](../core/Inno.Core.Diagnostics.md) · [Stats](Inno.Editor.Panel.Stats.md)

该项目同时订阅彼此独立的 `Inno.Core.Logging` 与 `Inno.Core.Diagnostics`，并把两种数据组合成统一的 Editor Console。目录和项目名使用 `Logging`，避免 `Log` 被 ignore 规则或输出目录约定误判。

## 组成

- `EditorConsole`：位于 `Inno.Editor.Diagnostics`，拥有有界日志、当前诊断、全局 fingerprint 分组和 Play Session 保留策略。
- `ConsolePanel`：只读取 `IEditorConsole` 的不可变 Snapshot，负责等级过滤、折叠、详情和操作表现。

当滚动条原本位于底部时，新日志会继续滚到底部；用户向上浏览后不会抢夺滚动位置。按钮、折叠三角、行距与颜色全部来自 `ImGuiWidget`、`EditorStyleMetrics` 和 `EditorPalette`。相邻 card 只使用一次标准 `ItemSpacing`，不会再叠加额外占位元素，因此展开与收起状态都保持确定、均匀的外部间距。

内部实现中，Console entry identity 由来源种类与完整 64 位序号共同组成。普通 Log 与 Diagnostic 的计数空间彼此独立，ImGui scope 会分别压入来源、序号高位和低位，不能直接使用 `long.GetHashCode()` 代替身份；后者会让部分正负序号产生相同的 32 位值，并导致 auto-resize child 错误复用另一张 card 的布局状态。

游戏脚本的 `InnoEngine.Logging.Log` 与 Editor Panel 消费的是同一 Core stream。生命周期日志只会在 Scene 真正执行 Runtime lifecycle 时产生；Edit Mode 中单纯切换 enabled 不会伪造 `OnEnable`/`OnDisable`。

Console 默认启用与 Unity 一致的 `Clear on Play`：Play 请求进入 `Compiling` 时先 `Flush` LogRouter，再清除 Console 内上一轮普通 Log；Compiler、Importer、Rendering 等 `DiagnosticHub` current report 不会被清除，因为它们描述的错误仍然存在。当前 Play Session 的 Debug/Info/Warn/Error/Fatal 在退出后全部保留，便于停止后检查；下一次 Play 开始时再统一清除。该策略位于 Settings 的 `Editor/Diagnostics/Console/Clear on Play`，默认值为 `true`，Apply、Undo 或 Redo 后立即作用于 Console backend。Console toolbar 不再保存或显示第二份开关。Collapse 仍是 Panel layout preference；日志内容、展开项和 filter 临时状态不持久化。手动 Clear 同时清除普通 Log 和当前 Diagnostic 展示，后续 producer 发布完整 report 时会重新出现仍然有效的诊断。

Collapse fingerprint 包含 `LogSessionId`。同一 Session 内非连续、且 message/location/stack 相同的 occurrence 会全局聚合；不同 Edit/Play Session 即使文本完全相同也不会合并，展开时仍保留每次 occurrence。

脚本编译使用 `Script Compiler` group，每次编译完成都通过 `Diagnostics.Set` 设置完整结果；下一次干净编译设置空集合后，旧 warning/error 自动消失。脚本 reload 使用独立的 `Script Reload` group，因此编译结果与状态迁移问题不会互相覆盖。非 Play 临时输出的普通 Log 仍保留到容量淘汰或用户点击 Clear。

Console 还会实时显示 Asset Import/Build/Catalog、Asset Source Database、Scene Workspace、Editor Workspace、Panel Activation 和 Project Persistence 的当前报告。Diagnostic 恢复时对应卡片自动消失；同一失败首次出现时写入的异常 Log 不会随之删除。这使 Console 同时保留“现在需要处理什么”和“过去发生过什么”，但两者不会混成一条不可清理的历史流。

Console card header 只显示等级，例如 `[Info]` 或 `[Error]`。展开后的详情通过 `Kind: Log` 或 `Kind: Diagnostic` 明确来源；元数据稳定按 Kind、File、Source、Session、Time 排列，未分配的 Session 行省略。Header/metadata 与 stack trace 之间的横线都使用同一张 card 推导出的 separator color，避免展开区域出现两种无语义差异的颜色。`Copy Full Entry` 文本也保留同一来源信息，避免等级相同的历史日志与当前诊断产生歧义。所有 card 都使用统一 Editor Action/Menu 系统提供右键菜单：

- `Copy Message`：复制诊断 code 与消息正文。
- `Copy Full Entry`：复制时间、等级、category、重复次数和源文件位置。

菜单复用全局 `EditorMenuRenderer` 与 Context Menu 样式，因此会正确捕获 hover/input，不把右键事件传递给后方 Panel 内容。

两个复制操作的 area/action ID 保存在项目根目录 `LoggingInteractionIds` 的稳定 `const string` 清单中，运行时菜单模型直接使用这些字符串。Console 的 `OnDraw` 只能由宿主生命周期 bridge 调用；Draw 异常会关闭并 quarantine 当前 generation 的 Panel，同时由 runtime 保证 window/menu ImGui 栈平衡，不影响后续 Panel。

该 Panel 当前没有单独的 EditorScripts facade；日志写入 API 由 Core Logging 的 Runtime scripting profile 导出。
