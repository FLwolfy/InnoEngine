# Inno.Editor.Panel.Hierarchy

[Editor 索引](README.md) · [Engine Scene](../engine/Inno.Engine.Scene.md) · [Inspector](Inno.Editor.Panel.Inspector.md)

该项目拥有 Scene workspace、Hierarchy Panel、Scene/GameObject Action、上下文菜单以及 Scene/GameObject 拖放排序。它通过共享 Assets 和 Scene 类型与其他 feature 协作，不引用 FileBrowser 或 Inspector project。

## 公共 API

| API | 作用 |
| --- | --- |
| `HierarchyAreas.Hierarchy` | Scene 行、GameObject 行和空白区域的统一 area。 |
| `HierarchyActions` | Create Scene/Object/Child、Set Active、Rename、Delete。 |
| `EditorSceneWorkspace` | Scene 新建、加载、dirty、保存、Save As 与资产路径同步。 |
| `HierarchyObjectDropTarget` | 带 before/after/into 几何语义的对象目标。 |
| `HierarchySceneDropTarget` | Scene 重排或 GameObject root drop 目标。 |

## 菜单与 Action

空白区域、Scene 和 GameObject 使用同一个 area，target 类型区分匹配：

```csharp
[EditorAction("scene.export", HierarchyAreas.Hierarchy)]
[EditorMenu(HierarchyAreas.Hierarchy, "Export/Scene Package", order: 500)]
public sealed class ExportSceneAction : EditorAction<GameScene>
{
    protected override void Execute(EditorActionContext<GameScene> context)
    {
    }
}
```

同一路径可由其他 target 类型贡献不同 Action。只有 `Query` 可见的最具体实现进入菜单。

## Scene 与排序行为

- 双击 SceneAsset 采用 additive load；已打开时只切换 active scene。
- 打开后选择 `GameScene`，不会被 FileBrowser entry 再次覆盖。
- Scene 行可拖动重排，最后一个已加载 Scene 不可删除。
- GameObject 可同级重排、改变 parent 或移动到 Scene root。
- ancestor 拖入 descendant 时先提升直属 child，避免形成循环。
- drop 完成后选择移动对象并请求展开目标。

Scene 排序只决定 Hierarchy/SceneManager 顺序；GameSystem 使用显式 `order`。Component 的 Inspector 上下移动仅改变序列化和显示顺序，不隐式改变脚本执行优先级。

## 保存

Command/Ctrl+S 是共享 `EditorActions.Save`，由本 feature 为 active Scene 提供实现。Scene 名称与 `.innoscene` 文件名同步；dirty Scene 在 Hierarchy 中显示斜体和 `*`。将 Scene/GameObject 拖到任意 Asset directory 字符串 target 时分别保存 SceneAsset/PrefabAsset。

## Scripting API

EditorScripts 使用 `InnoEditor.Hierarchy`，其中包括 area/action 常量、`EditorSceneWorkspace` 和公开 drop target。没有 global using。
