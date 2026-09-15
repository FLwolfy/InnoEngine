# Inno.Editor.Assets

[Editor 索引](README.md) · [Wiki 首页](../README.md) · [Material](Inno.Editor.Shaders.md) · [Pipeline](Inno.Editor.Rendering.md)

## 职责与边界

提供原生资产草稿的可复用生命周期，以及 File Browser 可发现的资产创建模板协议。Material 与 Pipeline 共用草稿实现；本项目不认识 Shader、Sprite 或具体插件配置。
资产源编解码、原子保存和引用上下文属于 AssetPipeline/AssetSourceStore，文档与撤销属于 EditorInteractions。
本项目不另建资产数据库、文档服务或 Undo 栈。

## Asset 创建扩展

`AssetCreationTemplate` 的派生关系是发现协议；`AssetCreationMenuAttribute` 只携带无法从 `AssetObject` 推导的稳定模板 ID、菜单路径、扩展名、默认文件名和排序。File Browser Registry 构造并持有全部模板，因此模板本身不重复声明实例 ID。一个插件新增资产类型时不需要修改 File Browser 中央分支：

```csharp
using System.Text;
using InnoEditor.Assets;
using InnoEngine.Assets;

public sealed class DialogueAsset : AssetObject;

public static class ProjectIds
{
    public const string dialogueCreation = "example.gameplay.asset-create.dialogue";
}

[AssetCreationMenu(
    ProjectIds.dialogueCreation,
    "Gameplay/Dialogue",
    ".dialogue",
    "New Dialogue",
    groupOrder: 400,
    separatorBeforeGroup: true)]
public sealed class DialogueCreationTemplate : AssetCreationTemplate<DialogueAsset>
{
    public override byte[] Encode(AssetCreationContext context, AssetObject asset)
        => Encoding.UTF8.GetBytes("speaker: narrator\ntext: \n");
}
```

`AssetCreationTemplate<TAsset>` 默认构造 `TAsset` 并调用 `AssetCreationContext.EncodeNative`，适合 Inno 原生结构化源。自定义文本或二进制 Importer 可 override `Encode`，因此协议支持任意具体 `AssetObject`，并不要求所有资产共享一种源格式。模板必须返回其声明的精确类型；空返回、重复 ID、重复菜单路径、非法扩展名或错误类型会使候选 Registry 明确失败，不会部分发布菜单。

创建模板与 `EditorAction` 不竞争职责。`AssetCreationMenu` Registry 只提供动态菜单模型与初始内容；每个菜单项都把模板 ID 作为参数派发给同一个 `CreateAssetCommand : EditorAction<string, string>`。因此快捷键、enabled 查询、目标目录验证和实际写盘仍只有一条 Action 执行链，新增资产类型不会新增一套交互系统。

“继承 `AssetObject`”本身不会自动生成 Create 项：扩展名、默认内容和是否存在有意义的空白资产无法可靠推导。Texture、Geometry、AudioClip、普通 Text/Binary 等外部导入资产应通过导入文件产生，不注册空白模板；Scene/Prefab 继续由各自的领域工作流创建。这里是语义分类，不是 File Browser 的类型白名单。

## Importer 与 Build Processor 身份

`AssetImporter<TAsset>` 和 `AssetBuildProcessor<TDefinition>` 只表达行为契约；可发现实现的不可变协议 ID 分别写在 `[AssetImporter(id)]` 与 `[AssetBuildProcessor(id)]` 中。Registry 先读取 Attribute、验证类型与重复 ID，再构造并绑定实例，因此扩展不再通过 `override importerId` 或 `override processorId` 返回常量。扩展名、部署范围和构建行为仍属于实例能力。重复 ID、重复扩展名或同一 definition 的多个 processor 都会使候选 Registry 整体失败。

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

创建协议的公开 API：

| API | 语义 |
| --- | --- |
| `AssetCreationMenuAttribute` | 声明稳定模板 ID、分层菜单路径、源扩展名、默认名称和组排序 |
| `AssetCreationTemplate` | 通过派生被发现，创建精确的 detached `AssetObject`，并允许覆盖源编码 |
| `AssetCreationTemplate<TAsset>` | 使用默认构造和原生结构化编码的常用实现 |
| `AssetCreationContext.EncodeNative` | 使用当前 AssetPipeline 的完整引用上下文编码原生结构化源 |
| `EditorAssets.EncodeNative` / `DecodeNative<TAsset>` | 使用当前 AssetPipeline 引用上下文捕获、恢复独立的原生资产值；供 Inspector 草稿与 reload-safe History 使用 |

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
只读安装资产禁止 Replace/Save。选择切换不是 Save；关闭确认由无可见 Panel 的共享 Document Service 负责。
