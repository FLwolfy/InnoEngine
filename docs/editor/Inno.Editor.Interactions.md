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

## EditorScripts facade

逻辑命名空间为：

- `InnoEditor.Interactions`
- `InnoEditor.Actions`
- `InnoEditor.Menus`
- `InnoEditor.DragDrop`

脚本只能看到明确导出的契约，不能看到 Router、Catalog、TypeCache snapshot 或实现侧 `Inno.*`。完全禁止 global using。
