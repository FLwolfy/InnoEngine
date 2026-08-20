# Inno.Editor.Panel.FileBrowser

[Editor 索引](README.md) · [Assets](../assets/README.md) · [Hierarchy](Inno.Editor.Panel.Hierarchy.md)

该项目完整拥有 File Browser feature：Tree/List/Grid 表现、导航与过滤、Asset selection、AssetEditor 扩展、文件操作 Action、菜单和 Asset drag source。它不引用 Hierarchy 或 Inspector。

## 公共扩展 API

| API | 作用 |
| --- | --- |
| `FileBrowserAreas.Browser` | Panel、entry 和背景右键菜单的稳定 area。 |
| `FileBrowserAreas.AssetReference` | Asset reference drop target 的共享 area。 |
| `FileBrowserActions` | CreateFolder、Rename、Delete 的 feature-owned ID。 |
| `AssetBrowserState` | 按 persistent identity 保存当前目录与选择。 |
| `AssetEditor` / `AssetEditorAttribute` | 为特定 Asset 类型声明 Open/Rename/Delete/Drag 行为。 |
| `AssetEditorContext` | 当前 `EditorContext`、interactions、路径、Asset 信息和实例。 |

## 为新 Asset 添加双击与右键行为

```csharp
using Inno.Editor.Panel.FileBrowser;

[AssetEditor(typeof(AnimationClipAsset), useForChildren: true, priority: 100)]
public sealed class AnimationClipEditor : AssetEditor
{
    public override bool CanOpen(AssetEditorContext context) => true;

    public override void Open(AssetEditorContext context)
    {
        // Open the animation feature without changing File Browser identity.
    }

    public override AssetOperationValidation ValidateDelete(
        AssetEditorContext context)
        => context.info.status == AssetImportStatus.Ready
            ? AssetOperationValidation.valid
            : AssetOperationValidation.Invalid("The clip is not ready.");
}
```

Asset Rename/Delete 的物理事务始终由 `AssetManager` 执行。AssetEditor 只能验证以及接收提交后的通知，不能自行移动 source/meta/artifact，因此外部文件变化与 Editor 操作拥有同一身份规则。

为某类 Asset 添加额外右键菜单只需普通 Action：

```csharp
[EditorAction("animation.reimport", FileBrowserAreas.Browser)]
[EditorMenu(FileBrowserAreas.Browser, "Animation/Reimport", order: 400)]
public sealed class ReimportAnimationAction : EditorAction<AssetFileEntry>
{
    protected override EditorActionState Query(
        EditorActionContext<AssetFileEntry> context)
        => context.target.extension == ".anim"
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        AssetManager.Import(context.target.relativePath);
    }
}
```

同一个菜单 Attribute 自动出现在 Tree/List/Grid，因为三种视图都提交相同 area 和共享 `AssetFileEntry` target。

## Rename 与打开

- 快速双击调用 AssetEditor 的 Open。
- 已选 entry 的慢速第二次单击启动本项目的 Rename Action。
- F2 和右键 Rename 调用同一个 Action。
- Rename Action 自己持有输入/验证状态；Tree/List/Grid 只调用 `Present` 绘制 inline editor。
- SceneAsset 打开 Action 由 Hierarchy feature 实现，但使用全局 Open 语义和共享路径参数，不形成 Panel project 引用。

## Scripting API

EditorScripts 使用 `InnoEditor.Assets`，可扩展 AssetEditor 并使用 FileBrowser area/action 常量。脚本必须显式写 `using InnoEditor.Assets;`。
