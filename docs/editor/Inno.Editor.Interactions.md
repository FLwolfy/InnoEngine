# Inno.Editor.Interactions

[Editor 索引](README.md) · [Core](Inno.Editor.Core.md) · [ImGui](Inno.Editor.ImGui.md)

`Inno.Editor.Interactions` 提供表现后端无关的交互语言：强类型 `EditorAreaId`、可选 `target`、轻量 `EditorInteraction`、强类型 command，以及 Attribute 自动发现的 Action、Menu、Shortcut 和 Drop。它不引用 ImGui、Assets、Scene 或任何 Panel project。

## 最小心智模型

```csharp
EditorInteraction interaction = interactions.For(
    AnimationAreas.GraphId,
    selectedState);

interaction.Focus();
interaction.Select();
interaction.Execute(AnimationActions.RenameStateCommand);
EditorMenuModel menu = interaction.BuildMenu();
```

- Attribute 因 metadata 限制继续使用 feature 自己维护的稳定 `const string`；运行时立即转换为 `EditorAreaId` / `EditorActionId`。
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
    public static readonly EditorAreaId GraphId = new(Graph);
    public static readonly EditorAreaId StateId = new(State);
}
```

命名建议使用小写、以 `/` 分层。area 不需要注册；第一次传给 `For` 或 Attribute 时即可使用。

## Action

立即完成的操作：

```csharp
public static class AnimationActions
{
    public const string DeleteState = "animation.state.delete";
    public const string RenameState = "animation.state.rename";
    public const string CreateTemplate = "animation.template.create";
    public static readonly EditorCommand DeleteStateCommand =
        new(new EditorActionId(DeleteState));
    public static readonly EditorCommand RenameStateCommand =
        new(new EditorActionId(RenameState));
    public static readonly EditorCommand<string> CreateTemplateCommand =
        new(new EditorActionId(CreateTemplate));
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

解析优先级为精确 area、target 类型距离和 Attribute priority。找不到或禁用时 `Execute` 返回 `false`；需要参数的实现继承 `EditorAction<TTarget,TArgument>` 或 `EditorArgumentAction<TArgument>`，dispatch 前会严格校验 command 参数类型，不向扩展暴露 `object argument` 或 `TryGetArgument`。

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

上述 Rename 以 `EditorPresentationAction<AnimationState,RenamePresentation>` 实现，并分别用同一 action id 的 `EditorCommand` 启动、`EditorCommand<RenamePresentation>` 呈现；presentation 数据始终是编译期强类型。

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
            AnimationActions.CreateTemplateCommand,
            "Humanoid");
    }
}
```

Action 的 `Query` 决定条目是否可见、可用、勾选和动态标题；快捷键标签从 `[EditorShortcut]` 自动生成。

快捷键显示与键盘 dispatch 共用同一个 resolver：先按 action id、area、target specificity 与 priority 解析实际 registration，再读取它的 gesture。相同优先级、相同 specificity 且 gesture 冲突会在 catalog Build 阶段作为歧义拒绝，不再用 `handledActions` 或类型名排序掩盖 contextual registration。

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

视图通过 `BeginDrag` 获得 token，再在目标 interaction 上调用 `QueryDrop`/`Drop`。每次真实 Begin 都生成新 token 并替换完整 session，即使 source 或 data reference 与上次相同也不会复用；token 只对当前 session 的同一 data reference 有效。managed payload 不进入 native 字节常量；generation 更新、source 失效或 drop 完成都会取消 token。

## Runtime 与热重载

`EditorInteractionRuntime` 从当前 TypeCache snapshot 原子构建 Module、Action、Menu source、Drop、Panel 和 Modal。候选冲突或构造失败会拒绝新 snapshot，旧 generation 继续工作。Host 类型实例会尽量保留；插件类型会 Detach/Stop/Dispose，避免固定旧 ALC。

candidate 激活前会取消 drag、清空 pending action/presentation/menu model。Selection 与 Focus 指向 retiring collectible 类型时先清除；若对象实现 `IIdentityObject`，则暂存 persistent ID 并在下一次 Update 尝试绑定当前 generation 对象，解析失败才保持清空。

候选 snapshot 作为 staging catalog 对生命周期回调可见，但重入刷新只记录为 deferred transition。激活时先按 `EditorModuleAttribute.order` Start Module，再 Attach 依赖它们的 Panel，最后对成功附加的扩展执行可容错 Restore；全部强制 Module 成功后才发布 active snapshot。Module Start 失败会逆序清理 candidate 并恢复旧 snapshot 与旧 History handler map；Panel Attach 失败只隔离该 Panel。全局 Complete 后才逆序 Stop/Detach retiring generation。被新旧 snapshot 共同保留的 Host instance 不重复 Start/Attach，也不会随旧 snapshot 被 Dispose。

无法 Attach 或 Draw 的 Panel 会被关闭并进入当前 generation quarantine；Module Update 失败会隔离当前 Module但继续后续更新；Modal 状态读取/Draw 失败会跳过当前 Modal。Stop/Detach/Dispose 异常只记清理诊断并继续。quarantine 只属于当前 snapshot，新 generation 会重新尝试，诊断按 extension id 去重并在恢复后清除。失败 Panel 不参与 Restore、Capture 或 Draw。

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

Core 的 internal layout document 在内存中分别维护 ImGui layout 和具名 Inno Editor sections，避免两类内容互相覆盖。只有 Interactions/Application internal host bridge 可以读写当前具名 section；扩展拿到的 `EditorContext` 没有 layout façade，不存在第二套 Module/Panel 状态文档。

Module/Panel 的恢复状态按实例弱跟踪。只有成功完成 protected `Restore` 的实例才能参与后续 capture；脚本启动、TypeCache 重建或 Registry 事务在恢复回调中触发重入刷新时，同一实例不会被再次调用，也不会用尚未初始化的默认字段覆盖磁盘 section。被新 snapshot 保留的 Module/Panel 仍以实际恢复状态为准，而不是仅因实例相同就跳过首次恢复。

Undo 栈、dirty Scene 内容、runtime 对象和编译中间态不会跨进程保存。它们要么无法安全跨代际恢复，要么本身可以由 Asset Database 和脚本构建图重建。

Selection 同样是当前 Editor session 的瞬时交互状态，不写入 `editor.ini`。Scene Workspace 只保存打开顺序与 active Scene；项目恢复完成后 selection 保持为空，直到用户或明确的打开操作重新选择对象。

Scene 内容历史由独立的 [Inno.Editor.Scene](Inno.Editor.Scene.md) 实现。它不会为一次小修改序列化整张 Scene 图，而是按属性、元素、子树、placement 或文档记录最小 payload。

## EditorScripts facade

物理源码无论位于 `Actions`、`Menus`、`DragDrop`、`Runtime` 或 `Selection`，都使用项目级 namespace `Inno.Editor.Interactions`。EditorScripts 对应的唯一逻辑 namespace 是 `InnoEditor.Interactions`。

脚本只能看到明确导出的契约，不能看到 Router、Catalog、TypeCache snapshot 或实现侧 `Inno.*`。完全禁止 global using。
