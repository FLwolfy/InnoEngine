# Inno.Editor.Graph

[返回 Editor 索引](README.md) · [Wiki 首页](../README.md) · [通用 Graph](../core/Inno.Core.Graphs.md) · [MaterialGraph](../render/Inno.Rendering.MaterialGraph.md)

`Inno.Editor.Graph` 提供不含 Shader 或 ImGui 语义的编辑控制层。`GraphEditorModule` 管理按稳定 document ID 索引的 session；`GraphDocumentController` 完成节点增删移动、连接重连、值修改、复制粘贴与 dirty/revision；`GraphCanvasState` 保存 session 内 pan/zoom、选择和 pending connection。

`GraphDocumentHistory`、`GraphHistoryData` 与 `GraphHistoryTransition` 把 before/after 文档编码为中立 bytes，经 `EditorInteractions.history` 执行。拖动可使用稳定 merge key；结构修改不合并。History payload 不保存 CLR `Type`、节点实例、GPU 对象或 delegate。

`GraphCanvasState.selectedNodes/selectedEdges` 返回冻结集合快照，不能强转 HashSet 绕过选择 API；它们仍是当前 Session 瞬时状态，不持久化到 editor.ini。

公开 API 由 `GraphEditorModule`、`GraphDocumentController`、`GraphClipboardData`、`GraphCanvasState` 组成。History codec/transition 是 internal 实现，不是扩展契约。

## 文档身份与恢复

| API | 当前契约 |
| --- | --- |
| `OpenDocument(Guid, GraphDocument, IEditorHistory)` | 新建或加入已有 session；Asset-backed 文档使用 Asset persistent ID，重接 UI 不覆盖 dirty 内容；最多同时拥有 128 个文档 |
| `TryOpenDocument(Guid, IEditorHistory, out controller)` | 只加入已有状态，不创建空白替代品 |
| `RebindDocument(Guid, GraphDocument)` | 仅未修改状态接受新导入内容，保留 dirty 文档 |
| `SetAvailability(Guid, bool)` | 源 Missing 时保留图、位置、属性 bytes 和 History；恢复后原 Undo/Redo 自动可用 |
| `CloseDocument(Guid)` | 显式注销 identity；已发出的 controller 永久失效，即使相同 persistent ID 重开 |
| `GraphDocumentController` | documentId/isAvailable/document/revision/isDirty、MarkSaved、AddNode/RemoveNodes/MoveNodes、Connect/Disconnect、SetNodeValue/RemoveNodeValue、Copy/Paste |

Module 的 live session 只由自己的 IdentityAllocator 解析，Guid 集合是次级目录，controller 保存弱 Identity 而非跨代 session 对象。Identity 索引本身不承担强引用保活；Module 的独立资源集合拥有 session，直到 CloseDocument 或 Module 退出时释放，不能把弱索引误当作 lifetime owner。OnStart/OnStop 自动注册/注销 IEditorReloadParticipant，Capture 使用 ReferenceRecoveryTransaction 五阶段。Prepare 捕获中立 Graph bytes；Apply 完整解码后发布；Validate 检查节点、边、顺序与属性不丢失；失败精确恢复 revision、dirty 和可用性。Graph 的 Stable Node ID 是语义，不强塞入 object reference slot。

GraphDocument 是可编辑创作模型，不是假称不可变的运行快照。回调/编译产物需要独立副本；GraphCanvasState 仅保存 session 瞬时展示状态。

相邻页面：[Inno.Core.Graphs](../core/Inno.Core.Graphs.md) · [MaterialGraph Panel](Inno.Editor.Panel.MaterialGraph.md) · [Editor Interactions](Inno.Editor.Interactions.md)
