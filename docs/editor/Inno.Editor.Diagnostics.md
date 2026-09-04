# Inno.Editor.Diagnostics

[Editor 索引](README.md) · [Logging Panel](Inno.Editor.Panel.Logging.md) · [Core Logging](../core/Inno.Core.Logging.md)

该 project 是 Editor Console 后端，不绘制 ImGui。它组合 `ILogSink` 与 `IDiagnosticSink`，按 Session identity 保存 occurrence、执行 Clear-on-Play policy，并生成不可变 collapse snapshot。

## 公开 API

- `IEditorConsole`：Panel/feature 使用的只读 snapshot 和过滤入口。
- `EditorConsole`：Application Composition Root 创建和拥有的实现。
- `EditorConsoleSnapshot`, `EditorConsoleGroup`, `EditorConsoleOccurrence`：不可变展示模型。
- `EditorConsoleEntryKind`：Log/Diagnostic domain。
- `IEditorConsole.clearOnPlay`：Console backend 的当前有效策略；正式用户入口位于 `Editor/Diagnostics/Console/Clear on Play` Settings，默认开启，current diagnostics 始终按 producer report 生命周期管理。

Fingerprint 包含 domain、severity、source、code、message、location、stack identity 和 `LogSessionId`；同一 Session 的非连续等价项全局聚合，不同 Session 不会误合并。Buffer、capacity queue、fingerprint builder 与 clear-on-play implementation 均保持 internal。

Console timeline 与 collapse group 都按 occurrence sequence 正序排列：最旧在上、最新在下。Panel 仅在用户已经位于底部或明确请求滚动时跟随新条目；用户向上查看历史后不会被强制拉回。因此新启动错误显示在最下方是正常的时间线语义，不是 Error 优先级异常。编译器、Importer、Rendering 等可恢复问题使用 `DiagnosticHub` 的当前状态报告；普通 `Log` 才保留历史调用栈。
