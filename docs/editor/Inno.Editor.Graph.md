# Inno.Editor.Graph

[返回 Editor 索引](README.md) · [Wiki 首页](../README.md) · [通用 Graph](../core/Inno.Core.Graphs.md) · [ShaderGraph](../render/Inno.Rendering.ShaderGraph.md)

`Inno.Editor.Graph` 提供不含 Shader 或 ImGui 语义的编辑控制层。`GraphEditorModule` 管理按稳定 document ID 索引的 session；`GraphDocumentController` 完成节点增删移动、连接重连、值修改、复制粘贴与 dirty/revision；`GraphCanvasState` 保存 session 内 pan/zoom、选择和 pending connection。

`GraphDocumentHistory`、`GraphHistoryData` 与 `GraphHistoryTransition` 把 before/after 文档编码为中立 bytes，经 `EditorInteractions.history` 执行。拖动可使用稳定 merge key；结构修改不合并。History payload 不保存 CLR `Type`、节点实例、GPU 对象或 delegate。

公开 API 由 `GraphEditorModule`、`GraphDocumentController`、`GraphClipboardData`、`GraphCanvasState` 及 Graph History codec/transition 组成。文档关闭会释放 session 引用，热重载后可用相同 ID 绑定新文档实例。

相邻页面：[Inno.Core.Graphs](../core/Inno.Core.Graphs.md) · [ShaderGraph Panel](Inno.Editor.Panel.ShaderGraph.md) · [Editor Interactions](Inno.Editor.Interactions.md)
