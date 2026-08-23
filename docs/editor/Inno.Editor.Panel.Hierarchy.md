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
- Scene 行可拖动重排，任意已加载 Scene 都可以关闭，包括最后一个 Scene。
- GameObject 可同级重排、改变 parent、移动到其他 Scene root，或直接成为其他 Scene 中对象的 child。
- ancestor 拖入 descendant 时先提升直属 child，避免形成循环。
- drop 完成后选择移动对象并请求展开目标。

Scene 排序只决定 Hierarchy/SceneManager 顺序；GameSystem 使用显式 `order`。Component 的 Inspector 上下移动仅改变序列化和显示顺序，不隐式改变脚本执行优先级。

Scene、GameObject 的创建、删除、排序、parent 修改、跨 Scene 移动、名称和 active 修改都会通过 `SceneEdits` 进入共享 `EditorHistory`。对象删除只保存被删子树与外部引用；层级修改只保存受影响对象的 scene/parent/sibling tuple，因此 Undo/Redo 可以把同一对象实例移回原 Scene；Scene 重排只保存两个 index，不会复制整张 Scene。

Hierarchy Panel 关闭根 window padding，正文 child 也不重复添加内边距，因此 Scene/GameObject 行与 Dock body 边缘对齐，不会出现双层外边缘空隙。Tree 行保持原有紧凑内容高度，只使用可缩放的 `hierarchyItemSpacing` 控制行距；guide 按真实行底边、间距和 overlap 连续延伸，不通过增加栏目高度连接线段，并在当前帧即时绘制，拖拽时不再依赖可能失效的上一帧线段。它仍与 FileBrowser Tree 保持相同滚动语义：普通短内容不会产生横向 scroll range，只有真实名称或深层缩进超出 viewport 时才允许必要的最小横向移动，并显示原生水平 scrollbar。Tree 内容、图标与 hit area 共享一个滚动坐标系；Scene、selection 和交替行背景固定覆盖可视宽度，不随 `ScrollX` 移出或在滚动帧变透明。Tree 行的可交互右边界采用 ImGui `WorkRect`，不再把 window padding 误算成内容溢出。行尾 active eye 使用 `drawViewportOverlay` 固定在 work region 右边界并内缩统一的 `windowPadding.X`；它使用方形 compact icon slot、按 glyph 可见边界垂直居中，同时不参与 Tree 内容宽度。Hierarchy 拉宽再缩窄时，旧眼睛位置和整行 hit area 不会形成持久 `ScrollMaxX`。

内容编辑 Command 必须创建可逆历史项；Scene/GameObject 创建删除、Component/System 增删 Reset、层级与顺序修改、名称/active/enabled 和序列化属性均属于内容编辑。Open Scene、Set Active Scene、Selection 和 Save 是导航或持久化命令，不修改可撤销内容，因此明确不进入 Undo 栈。新的 feature Command 若不支持 Undo，应同样只限于导航、查询、外部构建或不可逆操作，并在其 Wiki 中声明原因。

## 下次打开项目

Workspace 自动保存已打开且有 source path 的 Scene 顺序与 active Scene。Scene/GameObject/Component/System selection 不跨启动保存。恢复时 Scene 仍采用 additive load；缺失或无法加载的 Scene 被跳过并记录 warning。没有保存的 Scene、全部 Scene 被关闭或全部恢复失败时，Hierarchy 保持为空，不会隐式创建 Untitled Scene。

Scene setup 写在 `editor.ini` 的 `[InnoEditor][Module.scene-workspace]` 中，可直接阅读和编辑。正常关闭窗口时会在 Scene 被卸载前强制捕获一次；启动时若 Asset Database 或脚本类型尚未准备完成，则保留这些路径并重试，不会用空 Workspace 覆盖已保存 setup。

恢复会分别等待 Source Index 与脚本 TypeCache：源文件在磁盘存在但 Asset Database 尚未完成首轮对账时，不将它误判为 missing；Scene 中引用的脚本 Component/System 尚未激活时，也不会清空已保存路径。两项依赖都准备好后，候选 Scene 一次性 additive 提交并恢复 active Scene，selection 保持为空。

未保存 Scene 的完整内容和 dirty 修改不会隐式写进 Workspace。它们必须通过 Save 进入 `.iscene`；否则下次启动只恢复最后保存版本。这一边界避免项目状态文件悄悄成为第二份 Scene 数据库。

## 保存

Command/Ctrl+S 是共享 `EditorActions.Save`，由本 feature 提供实现，并自动出现在主菜单 `File/Save`。已有 source path 的 Scene 保存回原路径；从未保存的 Scene 以 File Browser 当前打开目录作为 fallback，不再固定落到 Assets 根目录。Scene 名称与 `.iscene` 文件名同步；dirty Scene 在 Hierarchy 中显示斜体和 `*`。将 Scene/GameObject 拖到任意 Asset directory 字符串 target 时分别保存 SceneAsset/PrefabAsset。

## Scripting API

EditorScripts 使用 `InnoEditor.Hierarchy` 获取 area/action 常量和公开 drop target；Workspace 与 Scene 编辑门面位于 `InnoEditor.Scene`。没有 global using。
