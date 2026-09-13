# Inno.Editor.Assets

[Editor 索引](README.md) · [Wiki 首页](../README.md) · [Material](Inno.Editor.Shaders.md) · [Pipeline](Inno.Editor.Rendering.md)

## 职责与边界

提供原生资产草稿的可复用生命周期。Material 与 Pipeline 共用此实现；不认识 Shader、Sprite 或具体插件配置。
资产源编解码、原子保存和引用上下文属于 AssetPipeline/AssetSourceStore，文档与撤销属于 EditorInteractions。
本项目不另建资产数据库、文档服务或 Undo 栈。

## 初始化与公开 API

Feature Module 在获得服务后创建 `AssetDraftDocuments<TAsset>(assets, serialization, interactions, providerId, historyKind, extension, label)`。
TAsset 必须是原生 AssetObject。providerId 和 historyKind 是稳定协议 ID，extension 包括前导点。
Feature 必须提供相同 historyKind 的 EditorHistoryHandler；不能保存回调式 Undo。

| API | 语义 |
| --- | --- |
| `Start()` | 注册共享文档 Provider；重复启动明确失败 |
| `Open(path)` | 打开来源与身份，不要求导入成功；不显示第二个 Inspector |
| `Read(assetId)` | 解码独立值；引用的 canonical 资产仍是只读输入 |
| `Replace(assetId, candidate, finishGesture)` | 修改草稿；手势结束记录一次 History，不写源 |
| `Commit(assetId)` | 结束手势，不保存 |
| `ReplaceMany(candidates, finishGesture)` / `CommitMany(ids)` | 多文档共用一次 History 事务；不是跨文件原子保存 |
| `GetDraft(assetId)` | 获取不含 Asset/Type/delegate 的中立状态对象 |
| `TouchInspection(assetId)` | 标记当前帧仍参与编辑；离开 Inspector 后完成遗留手势 |
| `ValidateHistory(change, direction)` / `ApplyHistory(change, direction)` | Feature 的注册 Handler 路由公共 History 协议，不独立移动历史栈 |
| `Update()` | 处理明确保存后的导入、源变动和离开 Inspector 的手势 |
| `Dispose()` | 保留脏草稿恢复数据，注销当前 Provider |

嵌套 `Draft` 只有公开只读属性：`id`、`documentId`、`path`、`readOnly`、`error`、`isDirty`。
它保存中立 bytes，不保存解码对象；状态属性不能绕过 Store 修改数据。

```csharp
using Inno.Assets;
using Inno.Editor.Assets;

// The feature owns this store and its registered History handler.
static void Rename<T>(AssetDraftDocuments<T> documents, AssetPath path, string name)
    where T : AssetObject
{
    var id = documents.Open(path);
    T draft = documents.Read(id);
    draft.name = name;
    documents.Replace(id, draft);
    // Save is a separate shared-document operation.
}
```

## Save、恢复与生命周期

Save/Revert/关闭脏文档使用共享 Document Service。Save 原子写源，下一次 Update 导入；导入失败明确显示已保存但失败。
Revert 读取最新源作为新基线并进入 History。Undo/Redo 只改草稿，再次 Save 才应用到运行时。
恢复文件位于 `Library/Editor/AssetDrafts/<providerId>/<persistent-id>.inno`，包含源指纹、基线和草稿。
外部冲突保留草稿且拒绝覆盖源。恢复写入失败时，内存草稿和历史保留，并在状态中明确报告风险。

Store 不保留插件资产实例、设置实例或 Type；消费者在每次绘制时 Read 并结束引用。
缺失源保留 bytes/history；缺失设置由领域层呈现，不清除中立属性。
只读安装资产禁止 Replace/Save。选择切换不是 Save；关闭确认由共享文档宿主负责。
