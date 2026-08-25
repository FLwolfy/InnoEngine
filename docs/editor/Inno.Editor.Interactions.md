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

一个 snapshot 激活时先 Attach 全部 Panel，再按 `EditorModuleAttribute.order` 启动 Module；关闭或替换时反向执行，先 Stop Module，再 Detach Panel。这样 Panel 的订阅与呈现对象在任何会产生启动日志或异步工作的 Module 之前就绪。Logging Module 使用基础设施最高优先级，保证其 sink 先于 Scripting Module 注册，因此首次脚本编译的 warning/error 也会进入 Console Panel。

无法 Attach 的 Panel 会被关闭并进入 `Panel Activation` 当前 Diagnostic；下一次 generation 中成功 Attach、Panel 被移除或 runtime 关闭时自动清除。Detach 是已经发生的 teardown 事件，只写 Log。Module/Panel 状态的 Capture、Restore 和 `editor.ini` Save 使用三个独立 Diagnostic group：周期重试成功只清除对应 group，完整异常仅在状态变化时进入 Log，避免两秒保存周期反复刷屏。

## Undo / Redo

每个 `EditorInteractions` 拥有一个 `EditorHistory`。History 不保存 Action 实例、Scene 对象、插件 `Type` 或来自 collectible ALC 的委托；稳定记录由四项组成：

- `kind`：全局唯一的协议 ID，例如 `animation/state-property`。
- `payload`：只含 ID、索引、字符串和序列化字节的中立数据。
- `mergeKey`：可选的连续编辑合并键。

领域 Module 在修改成功后使用 `RecordApplied`：

```csharp
byte[] data = AnimationHistoryData.Encode(
    controllerId,
    stateId,
    beforeName,
    afterName);

context.history.RecordApplied(
    "Rename Animation State",
    new EditorHistoryChange(
        "animation/state-name",
        EditorHistoryPayload.FromBytes(data),
        mergeKey: $"animation-state:{stateId}:name"));
```

当前 generation 的 Handler 由 TypeCache 自动发现：

```csharp
[EditorHistoryHandler("animation/state-name")]
public sealed class AnimationStateNameHistoryHandler : EditorHistoryHandler
{
    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        AnimationHistoryData data = AnimationHistoryData.Decode(
            change.payload.ReadBytes());
        return AnimationDatabase.Contains(data.controllerId, data.stateId)
            ? EditorHistoryAvailability.Available()
            : EditorHistoryAvailability.Unavailable("The animation state no longer exists.");
    }

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        AnimationHistoryData data = AnimationHistoryData.Decode(
            change.payload.ReadBytes());
        string value = direction == EditorHistoryDirection.Undo
            ? data.beforeName
            : data.afterName;
        AnimationDatabase.Rename(data.controllerId, data.stateId, value);
        return EditorHistoryResult.Success();
    }
}
```

`Query` 不修改状态，只给菜单和快捷键提供可用性与 barrier 原因。`Apply` 必须原子化：失败时自己回滚部分写入并返回 `Failure`。失败的 Undo/Redo 不移动栈指针，因此依赖恢复后可以重试。Handler 缺失、版本不兼容、目标删除或 Stable Type ID 不可解析都会成为明确 barrier，而不是丢弃记录或使用错误对象。

`Execute(name, EditorHistoryChange)` 适合 Handler 自己安全执行初次 Redo 的命令；多数 Editor UI 已先应用修改，因此使用 `RecordApplied`。委托式 `Execute`、`RecordValue` 与自定义 `EditorHistoryOperation` 只保留给 Host-only 兼容场景，它们属于 runtime-bound entry，在扩展 catalog generation 改变时自动截断，不能用于 EditorScripts 或长期历史。

相邻中立记录只有在 `kind`、非空 `mergeKey` 与 Handler 的 `TryMerge` 都匹配时才会合并。单击开关、创建、删除和排序不设置 merge key；拖动数值、连续文字输入等可合并编辑才设置。

### 事务与资源预算

多个已经独立可逆的修改可以组成一个顶层事务：

```csharp
using EditorHistoryTransaction transaction = context.history.BeginTransaction("Create Controller");
// Apply and RecordApplied each independent neutral child change.
transaction.Commit();
```

事务 Undo 按反序、Redo 按正序执行；任一 child 失败时回滚本次已经完成的 child。事务不会替代领域原子性：每个 Handler 仍必须保证自己的单步失败不泄漏半状态。

默认保留 256 个顶层记录，同时受 `EditorHistoryOptions.maxResidentBytes` 与 `maxDiskBytes` 限制。小 payload 驻留内存；达到 `inlinePayloadThreshold` 的 payload 自动进入 `<Project>/Library/Editor/History` session blob store。清空 History、淘汰记录、Runtime 关闭或丢弃 Redo branch 时立即释放对应文件。磁盘 payload 不随 Scene、Prefab、`.imeta` 或 `editor.ini` 持久化。

扩展 reload 会先构建新的 Handler Registry snapshot，再与 Action/Menu/Drop/Panel 一起原子激活。中立 History 不清空，后续 Undo/Redo 总是通过新 generation Handler 解释；旧 delegate entry 会被截断，避免固定旧 ALC。

内建 `Edit/Undo` 与 `Edit/Redo` 菜单自动显示下一操作名称。快捷键为 Command/Ctrl+Z、Command/Ctrl+Shift+Z，并额外支持 Command/Ctrl+Y。

## Module/Panel 状态存储

Interactions 从 `EditorModuleAttribute.id` / `EditorPanelAttribute.id` 取得唯一身份，并只为真正 override protected `Capture(EditorState)` 的 Module/Panel 建立内部状态注册。没有 override Capture 的实例不进入状态 IO，也不会创建空 section。项目语义状态写入：

```text
<Project>/editor.ini
```

每个有状态的 Module/Panel 使用独立、可读的 INI section。单个复杂值使用一行 JSON 表示数组或字符串，不使用 Base64，也不把全部状态包进 opaque payload：

```ini
[InnoEditor][Module.scene-workspace]
activeScene="Scenes/Main.iscene"
openScenes=["Scenes/Main.iscene","Scenes/UI.iscene"]

[InnoEditor][Panel.asset.file-browser]
filter=""
viewMode="List"
treePaneRatio=0.5
listNameSeparator=0.4
listTypeSeparator=0.7

[InnoEditor][Panels]
asset.file-browser=true
scene.hierarchy=true
```

文件通过临时文件 flush 后原子替换，并在运行期间进行约两秒的内容变化节流。`EditorInteractionRuntime.SaveState()` 可显式捕获并 flush；退出时 Application 会在扩展停止前强制捕获所有有状态实例和最新 ImGui layout，然后强制写入完整文档。未知 Module/Panel section 会保留，因此暂时移除插件不会销毁其设置；损坏的单个值由 `EditorState.Get` 回退。Panel 的 `isOpen` 按稳定 Panel ID 自动保存，即使 Panel 没有 override Capture 也不受影响。

Core 的 internal layout document 在内存中分别维护 ImGui layout 和具名 Inno Editor sections，避免两类内容互相覆盖。Interactions 只通过 `EditorContext` 的 layout façade 读取和写入当前具名 section，不存在第二套 Module/Panel 状态文档。

Module/Panel 的恢复状态按实例弱跟踪。只有成功完成 protected `Restore` 的实例才能参与后续 capture；脚本启动、TypeCache 重建或 Registry 事务在恢复回调中触发重入刷新时，同一实例不会被再次调用，也不会用尚未初始化的默认字段覆盖磁盘 section。被新 snapshot 保留的 Module/Panel 仍以实际恢复状态为准，而不是仅因实例相同就跳过首次恢复。

Undo 栈、dirty Scene 内容、runtime 对象和编译中间态不会跨进程保存。它们要么无法安全跨代际恢复，要么本身可以由 Asset Database 和脚本构建图重建。

Selection 同样是当前 Editor session 的瞬时交互状态，不写入 `editor.ini`。Scene Workspace 只保存打开顺序与 active Scene；项目恢复完成后 selection 保持为空，直到用户或明确的打开操作重新选择对象。

Scene 内容历史由独立的 [Inno.Editor.Scene](Inno.Editor.Scene.md) 实现。它不会为一次小修改序列化整张 Scene 图，而是按属性、元素、子树、placement 或文档记录最小 payload。

## EditorScripts facade

物理源码无论位于 `Actions`、`Menus`、`DragDrop`、`Runtime` 或 `Selection`，都使用项目级 namespace `Inno.Editor.Interactions`。EditorScripts 对应的唯一逻辑 namespace 是 `InnoEditor.Interactions`。

脚本只能看到明确导出的契约，不能看到 Router、Catalog、TypeCache snapshot 或实现侧 `Inno.*`。完全禁止 global using。
