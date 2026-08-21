# Inno.Editor.Interactions

[Editor 索引](README.md) · [Core](Inno.Editor.Core.md) · [ImGui](Inno.Editor.ImGui.md)

`Inno.Editor.Interactions` 提供表现后端无关的交互语言：一个字符串 `area`、一个可选 `target`、一个轻量 `EditorInteraction`，以及 Attribute 自动发现的 Action、Menu、Shortcut 和 Drop。它不引用 ImGui、Assets、Scene 或任何 Panel project。

## 最小心智模型

```csharp
EditorInteraction interaction = interactions.For(
    "panel/animation.graph",
    selectedState);

interaction.Focus();
interaction.Select();
interaction.Execute("animation.state.rename");
EditorMenuModel menu = interaction.BuildMenu();
```

- `area` 是稳定字符串，表示交互发生的位置，例如 `panel/asset.file-browser`。
- `target` 是当前实际对象，决定 typed Action/Drop 是否匹配。
- Action ID 表示语义操作，例如 `asset.rename`；它与 area 是两个正交维度。
- 不再需要 `Surface` marker type，也不需要创建 Context/Menu/Command service。

## 定义 area

每个 feature 在自己的项目中保留一个常量类：

```csharp
public static class AnimationAreas
{
    public const string Graph = "panel/animation.graph";
    public const string State = "panel/animation.graph/state";
}
```

命名建议使用小写、以 `/` 分层。area 不需要注册；第一次传给 `For` 或 Attribute 时即可使用。

## Action

立即完成的操作：

```csharp
public static class AnimationActions
{
    public const string DeleteState = "animation.state.delete";
}

[EditorAction(AnimationActions.DeleteState, AnimationAreas.State)]
public sealed class DeleteAnimationStateAction : EditorAction<AnimationState>
{
    protected override EditorActionState Query(
        EditorActionContext<AnimationState> context)
        => context.target.canDelete
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(
        EditorActionContext<AnimationState> context)
    {
        context.target.Delete();
    }
}
```

解析优先级为精确 area、target 类型距离、Attribute priority、完整类型名。找不到或禁用时 `Execute` 返回 `false`。

### 多帧 Action

Rename、分步创建和参数预览仍是普通 `EditorAction`。Action 自己持有状态，并调用 `Activate`、`Complete`、`Cancel`；视图只在目标位置调用 `Present`：

```csharp
protected override void Execute(EditorActionContext<AnimationState> context)
{
    m_name = context.target.name;
    Activate(context);
}

protected override bool Present(EditorActionContext<AnimationState> context)
{
    if (context.argument is not RenamePresentation presentation)
        return false;

    m_name = presentation.value;
    if (presentation.cancel)
        Cancel();
    else if (presentation.submit && TryCommit(context.target, m_name))
        Complete();
    return true;
}
```

没有单独的 `EditorActionInteraction<TState>` 或全局 Rename service。FileBrowser 和 Hierarchy 各自拥有 Rename Action，因为验证、提交和呈现目标属于各自 feature。

Selection 切换 target 时，Interactions 会通知旧 target 上仍活跃的多帧 Action 失去 presentation。默认实现取消操作；需要提交临时值的 Action 可以覆盖 `OnPresentationLost()`，在其中验证并调用 `Complete()`。如果覆盖返回时 Action 仍处于 active 状态，运行时会自动取消，避免不可见的输入操作永久残留。

## Menu

Action 可声明任意层级菜单路径：

```csharp
[EditorAction("animation.state.create", AnimationAreas.Graph)]
[EditorMenu(
    AnimationAreas.Graph,
    "Create/Animation/State",
    order: 200,
    separatorBefore: true)]
public sealed class CreateAnimationStateAction : EditorAction<AnimationGraph>
{
    protected override void Execute(EditorActionContext<AnimationGraph> context)
    {
    }
}
```

同一个 Attribute 同时适用于主菜单和右键菜单；区别只在 area。`EditorMenuRenderer` 会递归创建一级、二级或任意更深的菜单。

动态列表使用 `EditorMenuSource`：

```csharp
[EditorMenuSource(AnimationAreas.Graph)]
public sealed class AnimationTemplateMenu : EditorMenuSource
{
    public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
    {
        builder.Add(
            "Create/From Template/Humanoid",
            "animation.template.create",
            argument: "Humanoid");
    }
}
```

Action 的 `Query` 决定条目是否可见、可用、勾选和动态标题；快捷键标签从 `[EditorShortcut]` 自动生成。

## Selection 与 Focus

```csharp
EditorInteraction row = interactions.For(AnimationAreas.State, state);
if (clicked)
    row.Select();
if (panelFocused)
    row.Focus();
```

`Select()` 本身走内建 Action，因此和其他操作共享队列与代际规则。`EditorSelectionState` 只公开读取和 `TryGet<T>`，不公开可绕过 Action 的 mutator。

## Drag and Drop

```csharp
[EditorDrop(AnimationAreas.Graph)]
public sealed class ClipToStateDrop
    : EditorDrop<AnimationClipAsset, AnimationGraph>
{
    protected override EditorDropStatus Query(
        EditorDropContext<AnimationClipAsset, AnimationGraph> context)
        => EditorDropStatus.Accept(EditorDropVisual.Highlight);

    protected override EditorDropResult Drop(
        EditorDropContext<AnimationClipAsset, AnimationGraph> context)
    {
        AnimationState state = context.target.CreateState(context.source);
        return EditorDropResult.Accepted(selectionTarget: state);
    }
}
```

视图通过 `BeginDrag` 获得 token，再在目标 interaction 上调用 `QueryDrop`/`Drop`。managed payload 不进入 native 字节常量；generation 更新、source 失效或 drop 完成都会取消 token。

## Runtime 与热重载

`EditorInteractionRuntime` 从当前 TypeCache snapshot 原子构建 Module、Action、Menu source、Drop、Panel 和 Modal。候选冲突或构造失败会拒绝新 snapshot，旧 generation 继续工作。Host 类型实例会尽量保留；插件类型会 Detach/Stop/Dispose，避免固定旧 ALC。

## Undo / Redo

每个 `EditorInteractions` 拥有一个 `EditorHistory`。Action 通过 `context.history` 使用它，不需要额外注册，也不需要把 Action 设计成简单 inverse：

```csharp
EditorHistoryResult result = context.history.Execute(
    "Create State",
    execute: () =>
    {
        graph.CreateState(id);
        return EditorHistoryResult.Success();
    },
    undo: () =>
    {
        graph.RemoveState(id);
        return EditorHistoryResult.Success();
    });
```

初始 execute 失败时不会产生历史项。Undo 或 Redo 失败时，操作保持在原栈，因此目标恢复后可以重试，也可以由用户清空历史。执行新的操作会释放整个 Redo 分支；历史默认最多保留 256 个顶层操作，淘汰时调用 `Dispose()`，允许文件删除等操作清理暂存资源。

已经由 UI 应用的值使用 `RecordValue`，相同 `mergeKey` 的连续输入会合并为一项：

```csharp
target.weight = edited;
context.history.RecordValue(
    "Change Weight",
    before,
    edited,
    value => target.weight = value,
    $"weight:{target.id}");
```

多个修改使用事务；事务的 Undo 按反序执行，Redo 按正序执行，中途失败会尽力回滚已经完成的子步骤：

```csharp
using EditorHistoryTransaction transaction = context.history.BeginTransaction("Create Controller");
// Execute or RecordApplied child operations.
transaction.Commit();
```

复杂图操作应派生 `EditorHistoryOperation` 并保存中立快照，而不是只保存一个可能失效的对象引用。Scene feature 使用完整序列化图快照恢复对象 ID、引用、Component/System 和层级顺序。脚本 assembly generation 切换前会清空 history，防止旧插件实例被历史 delegate 固定。

内建 `Edit/Undo` 与 `Edit/Redo` 菜单自动显示下一操作名称。快捷键为 Command/Ctrl+Z、Command/Ctrl+Shift+Z，并额外支持 Command/Ctrl+Y。

## Workspace 存储

Interactions 自动协调 Core 的 `IEditorWorkspaceState` provider，并把项目语义状态写入：

```text
<Project>/editor.ini
```

每个 provider 使用独立、可读且带版本号的 INI section。单个复杂值使用一行 JSON 表示数组或字符串，不使用 Base64，也不把全部状态包进 opaque payload：

```ini
[InnoEditor][Project]
SchemaVersion=2

[InnoEditor][Module.scene-workspace]
Version=1
activeScene="Scenes/Main.innoscene"
openScenes=["Scenes/Main.innoscene","Scenes/UI.innoscene"]

[InnoEditor][Panel.asset-browser-panel]
Version=1
filter=""
viewMode="List"

[InnoEditor][Panels]
asset.file-browser=true
scene.hierarchy=true
```

文件通过临时文件 flush 后原子替换，并在运行期间进行约两秒的内容变化节流。退出时 Application 会在扩展停止前强制捕获所有 provider 和最新 ImGui layout，然后强制写入完整文档。未知 provider section 会保留，因此暂时移除插件不会销毁其设置；损坏的单个值只影响所属 provider。Panel 的 `isOpen` 按稳定 Panel ID 自动保存，不要求 Panel 实现接口。

`EditorProjectSettings` 在内存中分别维护 ImGui layout 和具名 Inno Editor sections，避免 ImGui 覆盖 workspace 或 workspace 覆盖布局。旧版 Base64 `[InnoEditor][Workspace]` 及 `Library/Editor/Workspace.json` 会自动迁移。

Workspace provider 的恢复状态按实例弱跟踪。只有成功完成 `RestoreWorkspaceState` 的 provider 才能参与后续 capture；脚本启动、TypeCache 重建或 Registry 事务在恢复回调中触发重入刷新时，同一个 provider 不会被再次调用，也不会用尚未初始化的默认字段覆盖磁盘 section。被新 snapshot 保留的 Module/Panel 仍以实际恢复状态为准，而不是仅因实例相同就跳过首次恢复。

Undo 栈、dirty Scene 内容、runtime 对象和编译中间态不会跨进程保存。它们要么无法安全跨代际恢复，要么本身可以由 Asset Database 和脚本构建图重建。

Scene 内容历史遵循两条规则：结构修改保存前后 Scene snapshot，并在保持 `GameScene` 实例的情况下重建其内部对象图；值修改若可能跨对象图重建，则只捕获 persistent ID，并在 Undo/Redo 时解析当前对象。历史操作不能保存可能已经 destroyed 的裸 Scene 对象回调。

## EditorScripts facade

物理源码无论位于 `Actions`、`Menus`、`DragDrop`、`Runtime` 或 `Selection`，都使用项目级 namespace `Inno.Editor.Interactions`。EditorScripts 对应的唯一逻辑 namespace 是 `InnoEditor.Interactions`。

脚本只能看到明确导出的契约，不能看到 Router、Catalog、TypeCache snapshot 或实现侧 `Inno.*`。完全禁止 global using。
