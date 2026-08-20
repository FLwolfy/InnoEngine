# Inno.Editor.Assets

[Editor 索引](README.md) · [Assets 系统](../assets/README.md) · [Scene Editor](Inno.Editor.Scene.md)

`Inno.Editor.Assets` 拥有 AssetEditor、Asset Browser、Asset selection、Asset command/menu 与 Asset-side drag/drop。它不包含 Scene 序列化或 Hierarchy 逻辑。

## AssetEditor

| API | 说明 |
| --- | --- |
| `AssetEditorAttribute` | 为精确 Asset type 或派生类型注册编辑器。 |
| `AssetEditor` | Rename/Delete validation、提交后通知和 drag data 扩展点。打开行为使用 typed `EditorAction`。 |
| `AssetEditorContext` | 当前 entry path/name/type、AssetInfo 与 EditorContext 的只读快照。 |
| `AssetOperationValidation` | Asset 操作的允许/拒绝结果。 |
| `AssetEditorModule` | 自动发现的 feature module；拥有 browser state、rename session 与 AssetEditor registry。通过构造注入供内建扩展协作。 |

```csharp
[AssetEditor(typeof(MaterialAsset), useForChildren: true, priority: 100)]
public sealed class MaterialAssetEditor : AssetEditor
{
    public override AssetOperationValidation ValidateDelete(AssetEditorContext context)
        => context.info?.status == AssetImportStatus.Imported
            ? AssetOperationValidation.valid
            : AssetOperationValidation.Invalid("Only imported materials can be deleted here.");
}
```

Rename/Delete 固定顺序为 Resolve Editor → Validate → `AssetManager` transaction → post-commit callback → selection/browser update。扩展不能绕过 AssetManager 直接修改 source/meta；post-commit callback 抛错只记录日志，不伪造回滚。

## Asset Browser

`FileBrowserPanel` 只负责 Tree/Table/Grid、搜索、导航、缩放和绘制状态。双击、Rename、Delete、context menu 和 drop 行为分别交给 Action、AssetEditor validation 与 typed Drop。

`AssetBrowserState` 保存当前目录，并以完整 relative path 创建 `AssetSelectionTarget`。`AssetSelectionTarget` 表示某个真实 entry，供 Open/Rename/Delete 使用；`AssetDirectoryTarget` 表示一个接收背景操作的目录，空字符串代表 Assets 根目录。List 名称只隐藏最后一层扩展名；selection、drag、open 与 transaction 始终使用完整路径。

Tree、List 与 Grid 的 entry 右键菜单统一解析为 `AssetSelectionTarget`。三个视图的空白区域以及 Tree 的 Assets 根节点统一解析为 `AssetDirectoryTarget`，内建 `Create/Folder` 在目标目录创建文件夹并立即进入 Rename。工具栏不再重复提供 New Folder 按钮。

Rename 在三个视图中都直接替换当前 entry 的名称文字，不再打开 modal popup。F2 和 context menu 会执行 Rename Action；该 Action 通过通用 `EditorActionInteraction<string>` 发布可编辑状态。对已选中 entry 的第二次慢单击会在系统双击判定时间结束后执行 Rename；快速双击则取消待定 Rename 并执行 Open。输入字体大小不变，输入框通过统一的 compact padding 与垂直 inset 放在原行内部。Enter 完成 Interaction，Escape 取消，验证失败保留状态供继续修改。

## Surfaces 与 actions

| 常量 | 值/用途 |
| --- | --- |
| `typeof(AssetSurface.Browser)` | Asset Browser focus、open 与 drag source。 |
| `typeof(AssetSurface.ContextMenu)` | Asset entry 与目录背景共用的 context menu；通过 target 类型区分操作。 |
| `typeof(AssetSurface.Reference)` | AssetReference property drop target。 |
| `AssetActionIds.CreateFolder` | 创建目录。 |

内建 context menu 直接声明在 Action 上：

```csharp
[EditorAction("asset.create-material", typeof(AssetSurface.ContextMenu))]
[EditorMenu(typeof(AssetSurface.ContextMenu), "Create/Rendering/Material", order: 300)]
public sealed class CreateMaterialAction : EditorAction<AssetSelectionTarget>
{
    protected override void Execute(EditorActionContext<AssetSelectionTarget> context) { }
}
```

面向空白区域的创建命令使用目录目标：

```csharp
[EditorAction("asset.create-material", typeof(AssetSurface.ContextMenu))]
[EditorMenu(typeof(AssetSurface.ContextMenu), "Create/Rendering/Material", order: 300)]
public sealed class CreateMaterialAction : EditorAction<AssetDirectoryTarget>
{
    protected override void Execute(EditorActionContext<AssetDirectoryTarget> context)
    {
        string targetDirectory = context.target.relativePath;
        // Create the material through AssetManager in targetDirectory.
    }
}
```

## Drag/drop 类型

`AssetDragSource` 保存 persistent ID、relative path 和 Asset type。`AssetDirectoryDropTarget` 表示目录，`AssetReferenceDropTarget` 表示属性赋值目标。Scene/Prefab 保存处理器位于 Scene feature，因为它们依赖 GameScene/GameObject；通用 Asset 模块不反向引用 Scene。

## Scripting facade

EditorScripts 使用 `InnoEditor.Assets` 和 `InnoEditor.DragDrop`。导出扩展契约、context、typed surface/action IDs 与目标模型；Registry 和 transaction 内部实现不作为脚本稳定 API。
