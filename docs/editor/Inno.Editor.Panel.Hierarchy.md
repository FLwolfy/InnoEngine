# Inno.Editor.Panel.Hierarchy

[Editor 索引](README.md) · [Editor Scene](Inno.Editor.Scene.md) · [Inspector](Inno.Editor.Panel.Inspector.md)

该项目拥有 Hierarchy Panel、Scene/GameObject Action、上下文菜单以及 Scene/GameObject 拖放排序。Scene document 和可逆编辑实现位于独立的 `Inno.Editor.Scene`；Hierarchy 只把用户意图交给 `EditorSceneWorkspace` / `SceneEdits`，不维护图快照或 Undo 实现。

## 公共 API

| API | 作用 |
| --- | --- |
| `HierarchyAreas.Hierarchy` | Scene 行、GameObject 行和空白区域的统一 area。 |
| `HierarchyActions` | Create Scene/Object/Child、Set Active、Rename、Delete。 |
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

Scene、GameObject 的创建、删除、排序、parent 修改、名称和 active 修改都会通过 `SceneEdits` 进入共享 `EditorHistory`。对象删除只保存被删子树与外部引用；层级修改只保存受影响对象的 parent/sibling tuple；Scene 重排只保存两个 index，不会复制整张 Scene。

内容编辑 Command 必须创建可逆历史项；Scene/GameObject 创建删除、Component/System 增删 Reset、层级与顺序修改、名称/active/enabled 和序列化属性均属于内容编辑。Open Scene、Set Active Scene、Selection 和 Save 是导航或持久化命令，不修改可撤销内容，因此明确不进入 Undo 栈。新的 feature Command 若不支持 Undo，应同样只限于导航、查询、外部构建或不可逆操作，并在其 Wiki 中声明原因。

## 下次打开项目

Workspace 自动保存已打开且有 source path 的 Scene 顺序、active Scene，以及可稳定重建的 Scene/GameObject/Component/System selection。恢复时 Scene 仍采用 additive load；缺失或无法加载的 Scene 被跳过并记录 warning，全部失败时创建新的 Untitled Scene。

Scene setup 写在 `editor.ini` 的 `[InnoEditor][Module.scene-workspace]` 中，可直接阅读和编辑。正常关闭窗口时会在 Scene 被卸载前强制捕获一次；启动时若 Asset Database 或脚本类型尚未准备完成，则保留这些路径并重试，不会用临时 Untitled Scene 覆盖已保存 setup。

恢复会分别等待 Source Index 与脚本 TypeCache：源文件在磁盘存在但 Asset Database 尚未完成首轮对账时，不将它误判为 missing；Scene 中引用的脚本 Component/System 尚未激活时，也不会清空已保存路径。两项依赖都准备好后，候选 Scene 一次性 additive 提交并恢复 active Scene 与 selection。

未保存 Scene 的完整内容和 dirty 修改不会隐式写进 Workspace。它们必须通过 Save 进入 `.innoscene`；否则下次启动只恢复最后保存版本。这一边界避免项目状态文件悄悄成为第二份 Scene 数据库。

## 保存

Command/Ctrl+S 是共享 `EditorActions.Save`，由本 feature 为 active Scene 提供实现。Scene 名称与 `.innoscene` 文件名同步；dirty Scene 在 Hierarchy 中显示斜体和 `*`。将 Scene/GameObject 拖到任意 Asset directory 字符串 target 时分别保存 SceneAsset/PrefabAsset。

## Scripting API

EditorScripts 使用 `InnoEditor.Hierarchy` 获取 area/action 常量和公开 drop target；Workspace 与 Scene 编辑门面位于 `InnoEditor.Scene`。没有 global using。
