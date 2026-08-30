# Inno.Editor.Interactions

[Editor 索引](README.md) · [Core](Inno.Editor.Core.md) · [ImGui](Inno.Editor.ImGui.md)

`Inno.Editor.Interactions` 提供表现后端无关的交互语言：稳定的 `string` area/action/panel ID、可选 `target`、轻量 `EditorInteraction`，以及 Attribute 自动发现的 Action、Menu、Shortcut 和 Drop。它不引用 ImGui、Assets、Scene、Scripting 或任何 Panel project；跨 feature reload 协议位于 [Inno.Editor.Core](Inno.Editor.Core.md)。

## 最小心智模型

```csharp
EditorInteraction interaction = interactions.For(
    AnimationInteractionIds.C_GRAPH_AREA,
    selectedState);

interaction.Focus();
interaction.Select();
interaction.Execute(AnimationInteractionIds.C_RENAME_STATE);
EditorMenuModel menu = interaction.BuildMenu();
```

- Attribute 和运行时 API 都直接使用 feature 自己维护的稳定 `const string`。
- `target` 是当前实际对象，决定 typed Action/Drop 是否匹配。
- Action ID 表示语义操作，例如 `asset.rename`；它与 area 是两个正交维度。
- 旧的强类型 ID/command wrapper 已删除，不存在兼容 wrapper 或转发 overload。
- 不需要 `Surface` marker type，也不需要创建 Context/Menu/Command service。

## 定义 area

每个 feature 在自己的项目根目录保留一个常量类：

```csharp
internal static class AnimationInteractionIds
{
    internal const string C_GRAPH_AREA = "panel/animation.graph";
    internal const string C_STATE_AREA = "panel/animation.graph/state";
    internal const string C_DELETE_STATE = "animation.state.delete";
    internal const string C_RENAME_STATE = "animation.state.rename";
    internal const string C_CREATE_TEMPLATE = "animation.template.create";
}
```

命名建议使用小写、以 `/` 分层。area 不需要注册；第一次传给 `For` 或 Attribute 时即可使用。所有公开入口对空白 ID 抛出 `ArgumentException`，匹配使用 ordinal string comparison。

## Action

立即完成的操作：

```csharp
[EditorAction(
    AnimationInteractionIds.C_DELETE_STATE,
    AnimationInteractionIds.C_STATE_AREA)]
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

解析优先级为精确 area、target 类型距离和 Attribute priority。找不到或禁用时 `Execute` 返回 `false`；需要参数的实现继承 `EditorAction<TTarget,TArgument>` 或 `EditorArgumentAction<TArgument>`，调用方使用 `Execute(actionId, argument)`，dispatch 前严格校验 action argument 类型。扩展仍只看到强类型 context，不暴露原始 `object argument` 或 `TryGetArgument`。

### 多帧 Action

Rename、分步创建和参数预览仍是普通 `EditorAction`。Action 自己持有状态，并调用 `Activate`、`Complete`、`Cancel`；视图只在目标位置调用 `Present`：

```csharp
protected override void Execute(EditorActionContext<AnimationState> context)
{
    m_name = context.target.name;
    Activate(context);
}

protected override bool Present(
    EditorActionContext<AnimationState, RenamePresentation> context)
{
    RenamePresentation presentation = context.argument;
    m_name = presentation.value;
    if (presentation.cancel)
        Cancel();
    else if (presentation.submit && TryCommit(context.target, m_name))
        Complete();
    return true;
}
```

上述 Rename 以 `EditorPresentationAction<AnimationState,RenamePresentation>` 实现。调用方用同一 action ID 的 `Execute(id)` 启动、`Present(id, presentation)` 呈现；presentation 数据在 Action 实现中始终是编译期强类型。Action 执行或呈现抛异常时，运行时会尝试取消其活动状态；取消回调本身失败也会被独立记录，不会破坏后续 Action。

没有单独的 `EditorActionInteraction<TState>` 或全局 Rename service。FileBrowser 和 Hierarchy 各自拥有 Rename Action，因为验证、提交和呈现目标属于各自 feature。

Selection 切换 target 时，Interactions 会通知旧 target 上仍活跃的多帧 Action 失去 presentation。默认实现取消操作；需要提交临时值的 Action 可以覆盖 `OnPresentationLost()`，在其中验证并调用 `Complete()`。如果覆盖返回时 Action 仍处于 active 状态，运行时会自动取消，避免不可见的输入操作永久残留。

## Menu

Action 可声明任意层级菜单路径：

```csharp
[EditorAction("animation.state.create", AnimationInteractionIds.C_GRAPH_AREA)]
[EditorMenu(
    AnimationInteractionIds.C_GRAPH_AREA,
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
[EditorMenuSource(AnimationInteractionIds.C_GRAPH_AREA)]
public sealed class AnimationTemplateMenu : EditorMenuSource
{
    public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
    {
        builder.Add(
            "Create/From Template/Humanoid",
            AnimationInteractionIds.C_CREATE_TEMPLATE,
            argument: "Humanoid");
    }
}
```

Action 的 `Query` 决定条目是否可见、可用、勾选和动态标题；快捷键标签从 `[EditorShortcut]` 自动生成。

Panel 主菜单使用同一棵层级菜单模型。`EditorPanelAttribute.menuPath` 是 `Panel/` 下的开放分类路径，支持任意斜杠层级；`separatorBefore` 在条目所在分类内开启视觉分组。Host 不维护封闭类别枚举，内置 Panel 当前按 Workspace、Viewports、Authoring、Content 与 Diagnostics 分类，Plugin Panel 可以声明自己的稳定分类而无需修改 Editor。

快捷键显示与键盘 dispatch 共用同一个 resolver：先按 action ID、area、target specificity 与 priority 解析实际 registration，再读取它的 gesture。同一 Action 可声明多个不同 gesture，每个都可 dispatch；菜单只显示当前 area 的第一个可用 gesture。精确 area shortcut 存在时会覆盖该 registration 的 global shortcuts。同一 Action 在同一有效 area 重复同一 gesture，或不同 Action 形成同 specificity 歧义，都会在 catalog Build 阶段被拒绝。

## Selection 与 Focus

```csharp
EditorInteraction row = interactions.For(AnimationInteractionIds.C_STATE_AREA, state);
if (clicked)
    row.Select();
if (panelFocused)
    row.Focus();
```

`Select()` 本身走内建 Action，因此和其他操作共享队列与代际规则。`EditorSelectionState` 只公开读取和 `TryGet<T>`，不公开可绕过 Action 的 mutator。

## Drag and Drop

```csharp
[EditorDrop(AnimationInteractionIds.C_GRAPH_AREA)]
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

视图通过 `BeginDrag` 获得 token，再在目标 interaction 上调用 `QueryDrop`/`Drop`。`BeginDrag` 先同步当前 extension generation，再创建属于该 generation 的 token，避免首次 drop resolver 刷新时立即取消刚创建的 session。每次真实 Begin 都生成新 token 并替换完整 session，即使 source 或 data reference 与上次相同也不会复用；token 只对当前 session 的同一 data reference 有效。managed payload 不进入 native 字节常量；generation 更新、source 失效、validity predicate 返回 `false` 或抛异常、drop 完成都会取消 token。predicate 异常只拒绝本次 drag session。

## Runtime 与热重载

`EditorInteractionRuntime` 从当前 TypeCache snapshot 原子构建 Module、Action、Menu source、Drop、Panel 和 Modal。候选冲突或构造失败会拒绝新 snapshot，旧 generation 继续工作。Host 类型实例会尽量保留；插件类型会 Detach/Stop/Dispose，避免固定旧 ALC。

candidate 激活前会取消 drag、清空 pending action/presentation/menu model。Selection 与 Focus 指向 retiring collectible 类型时先清除；若对象实现 `IIdentityObject`，则暂存 persistent ID 并在下一次 Update 尝试绑定当前 generation 对象，解析失败才保持清空。

候选 snapshot 作为 staging catalog 对生命周期回调可见。重入请求的新 rebuild 只能在当前全局 Complete 之后作为独立 transaction 运行；已完成的 Registry transaction 不再执行可失败的 pending refresh。激活时先按 `EditorModuleAttribute.order` Start Module，再 Attach 依赖它们的 Panel，最后对成功附加的扩展执行可容错 Restore；全部强制 Module 成功后才发布 active snapshot。Module Start 失败会逆序清理 candidate 并恢复旧 snapshot 与旧 History handler map；Panel Attach 失败只隔离该 Panel。全局 Complete 后才逆序 Stop/Detach retiring generation。被新旧 snapshot 共同保留的 Host instance 不重复 Start/Attach，也不会随旧 snapshot 被 Dispose。

无法 Attach 或 Draw 的 Panel 会被关闭并进入当前 generation quarantine；Panel `useWindowPadding` 和 Module `blocksFollowingUpdates` 这类扩展虚属性也在单实例边界内读取，getter 抛异常只隔离所属扩展。Module Update 失败会隔离当前 Module 但继续后续更新；Modal 状态读取/Draw 失败会跳过当前 Modal。Stop/Detach 异常只记清理诊断并继续；snapshot 释放时每个 `IDisposable` 实例单独 `try/catch`，前一个失败不会跻过后续实例。quarantine 只属于当前 snapshot，新 generation 会重新尝试，诊断按 extension ID 去重并在恢复后清除。失败 Panel 不参与 Restore、Capture 或 Draw。

Editor 正常关闭先捕获和原子写入最终状态，再 Stop Module、Detach Panel，然后才清空 Action/drag 并 Dispose History，最后逐实例 Dispose extension snapshot。任一阶段失败不会跳过后续阶段；全部退场尝试完成后再聚合报告宿主级失败。

## Undo / Redo

`EditorInteractions.history` 向扩展返回 `IEditorHistory`。具体 `EditorHistory`、delegate operation、`RecordValue`、handler-map 更新、Clear/Dispose 都是 host-only internal 能力；脚本只能提交中立 change，不能把 collectible delegate、`Type` 或 runtime object 固定在栈中。稳定记录由下列项组成：

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

`Query` 不修改状态，只给菜单和快捷键提供可用性与 barrier 原因。`Apply` 必须在修改前捕获最小 rollback state；普通 `Failure` 表示操作失败但输入状态已严格保留，失败的 Undo/Redo 不移动栈指针，可以重试。只有 History 内部可以生成 `statePreserved=false` 的状态完整性丢失结果；此时 History 进入 faulted，拒绝继续记录、Undo 和 Redo，直到宿主显式 Clear。Handler 缺失、目标删除或 Stable Type ID 不可解析是可诊断 barrier，不会丢弃记录或使用错误对象。

`Execute(name, EditorHistoryChange)` 适合 Handler 自己安全执行初次 Redo 的命令；多数 Editor UI 已先应用修改，因此使用 `RecordApplied`。业务源码不得使用 delegate operation、派生 `EditorHistoryOperation` 或 runtime object merge key。

相邻中立记录只有在 `kind`、非空 `mergeKey` 与 Handler 的 `TryMerge` 都匹配时才会合并。单击开关、创建、删除和排序不设置 merge key；拖动数值、连续文字输入等可合并编辑才设置。

### 事务与资源预算

多个已经独立可逆的修改可以组成一个顶层事务：

```csharp
using EditorHistoryTransaction transaction = context.history.BeginTransaction("Create Controller");
// Apply and RecordApplied each independent neutral child change.
transaction.Commit();
```

事务 Undo 按反序、Redo 按正序执行；任一 child 失败时验证并执行相反方向补偿。全部补偿成功返回原失败且顶层栈不移动；任一补偿失败会 fault History。显式 Rollback 只有完整成功后才出栈和释放，普通失败时 transaction 与 child 保持可重试；未 Commit 的 Dispose 若遇到状态仍完整的回滚失败，会把仍然应用的 transaction 提交到 Undo 栈并抛出明确异常，避免修改成为无 History 状态。

默认保留 256 个顶层记录，同时受 `EditorHistoryOptions.maxResidentBytes` 与 `maxDiskBytes` 限制。小 payload 驻留内存；达到 `inlinePayloadThreshold` 的 payload 自动进入 `<Project>/Library/Editor/History` session blob store。清空 History、淘汰记录、Runtime 关闭或丢弃 Redo branch 时立即释放对应文件。磁盘 payload 不随 Scene、Prefab、`.imeta` 或 `editor.ini` 持久化。

扩展 reload 对 Handler map 执行 Prepare → Activate → Rollback/Complete。Activate 只临时切换 candidate map；其他 registry 稍后失败时 Rollback 恢复旧 map，只有全局 Complete 才释放旧 handler generation 并丢弃 reload-unsafe host entry。kind 冲突在 candidate Build 阶段拒绝，不进入生命周期回调。

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

Core 的 layout document 在内存中分别维护 ImGui layout 和具名 Inno Editor sections，避免两类内容互相覆盖。只有 Interactions/Application 的 host CLR 路径会调用这些标记为 `ScriptingApiIgnore` 的成员；扩展拿到的脚本 facade 没有 layout API，也不存在第二套 Module/Panel 状态文档。

Module/Panel 的恢复状态按实例弱跟踪。只有成功完成 protected `Restore` 的实例才能参与后续 capture；脚本启动、TypeCache 重建或 Registry 事务在恢复回调中触发重入刷新时，同一实例不会被再次调用，也不会用尚未初始化的默认字段覆盖磁盘 section。被新 snapshot 保留的 Module/Panel 仍以实际恢复状态为准，而不是仅因实例相同就跳过首次恢复。

Undo 栈、dirty Scene 内容、runtime 对象和编译中间态不会跨进程保存。它们要么无法安全跨代际恢复，要么本身可以由 Asset Database 和脚本构建图重建。

Selection 同样是当前 Editor session 的瞬时交互状态，不写入 `editor.ini`。Scene Workspace 只保存打开顺序与 active Scene；项目恢复完成后 selection 保持为空，直到用户或明确的打开操作重新选择对象。

Scene 内容历史由独立的 [Inno.Editor.Scene](Inno.Editor.Scene.md) 实现。它不会为一次小修改序列化整张 Scene 图，而是按属性、元素、子树、placement 或文档记录最小 payload。

## EditorScripts facade

物理源码无论位于 `Actions`、`Menus`、`DragDrop`、`Runtime` 或 `Selection`，都使用项目级 namespace `Inno.Editor.Interactions`。EditorScripts 对应的唯一逻辑 namespace 是 `InnoEditor.Interactions`。

脚本只能看到明确导出的契约，不能看到 Router、Catalog、TypeCache snapshot 或实现侧 `Inno.*`。完全禁止 global using。
