# Inno.Editor.Inspection

[Editor 索引](README.md) · [Inspector Panel](Inno.Editor.Panel.Inspector.md) · [Interactions](Inno.Editor.Interactions.md)

该项目是与具体 Panel 无关的检查绘制基础设施。它拥有 `InspectionDrawer<TTarget>`、`PropertyDrawer` 契约、TypeCache Registry、draw context、serialized property renderer 以及 bool、number、string、enum、collection、struct、nullable、math 等通用内建 PropertyDrawer。

`Inno.Editor.Panel.Inspector` 只负责窗口、统一 Target Header 和 Scene 的 Component/System 操作；其他 feature 不需要引用 Inspector Panel。Feature project 只需引用 `Inno.Editor.Inspection`，把自己的内部 Drawer 与目标类型放在同一个项目中，TypeCache 会自动发现它。

## InspectionDrawer

```csharp
using System;

using Inno.Editor.Inspection;
using Inno.Adapter.Presentation.ImGui;

[InspectionDrawer(typeof(AnimationController))]
internal sealed class AnimationControllerInspectionDrawer
    : InspectionDrawer<AnimationController>
{
    public override string icon => ImGuiIcon.DiagramProject;

    protected override (string name, Action<string>? setter) BindName(
        InspectionDrawContext context,
        AnimationController target)
        => (target.name, null);

    protected override void Draw(
        InspectionDrawContext context,
        AnimationController target)
    {
        // Draw the feature-owned target without referencing InspectorPanel.
    }
}
```

`BindName` 在一次调用中返回当前显示名称和可选 setter。只读名称返回 `(name, null)`；可编辑名称返回 `(name, value => ...)`。Inspector Header 只解析一次该 tuple，避免名称值和编辑能力来自不同快照。

解析顺序是 exact target、继承距离、priority、稳定类型名。Registry 在新的 TypeCache generation 上旁路构建完整 snapshot；构造失败或 registration 冲突不会替换当前可用 snapshot。

`InspectionDrawContext` 提供：

- 当前 `EditorContext`。
- 当前 `EditorInteractions`。
- 当前 target。
- `SerializedPropertyRenderer`。

通用 context 不包含 `SceneEdits`、AssetPipeline 或其他 feature service。具体 Drawer 需要领域能力时，由宿主组合根通过构造函数注入。例如 GameObject/Scene Drawer 在 Inspector Panel 内部取得 `SceneEdits`，而资产 Drawer 只取得资产 icon provider。

## PropertyDrawer

```csharp
using Inno.Editor.Inspection;

[PropertyDrawer(typeof(AnimationCurve), priority: 100)]
internal sealed class AnimationCurvePropertyDrawer : IPropertyDrawer
{
    public void Draw(PropertyDrawContext context)
    {
        AnimationCurve value = (AnimationCurve)context.GetValue();
        // Draw and call context.SetValue(updatedValue) after an edit.
    }
}
```

PropertyDrawer 通过 declared property type 匹配。`PropertyDrawContext.SetValue` 会把修改交给所属 feature 的 edit service，由它写入中立的 Undo/Redo payload；Drawer 不应该绕过 context 直接修改 serialized owner。

`SerializedPropertyRenderer` 本身不依赖 Scene。创建 renderer 的 feature 提供 `IInspectionPropertyEditService`，负责把通用的 owner、root property 与 mutation 转换成自己的 Undo/Redo 协议。Inspector Panel 使用 Scene adapter；未来 Material、Animation 或 RenderGraph 检查器可以使用各自的 history adapter，而不用把 Scene 引入通用 Inspection 项目。

## Inspector presentation attributes

GameScripts 可以通过独立的 [Inno.Editor.Annotations](Inno.Editor.Annotations.md) 在 `SerializableProperty` 之外组合纯展示 Attribute；它们不决定成员是否持久化，也不改变序列化 schema：

```csharp
using InnoEngine.Serialization;
using InnoEditor.Annotations;

public sealed class CharacterPresentation : ISerializable
{
    [SerializableProperty]
    [Header("Movement", "Values are authored in world units per second.")]
    [InspectorName("Move Speed")]
    [Tooltip("Maximum planar speed.")]
    [Range(0, 30)]
    public float speed { get; set; } = 6f;

    [SerializableProperty]
    public bool useAcceleration { get; set; }

    [SerializableProperty]
    [ShowIf(nameof(useAcceleration))]
    [Range(0, 100)]
    public float acceleration { get; set; } = 20f;
}
```

`Header` 的 description 是标题的 hover tooltip，不占用 Inspector 的纵向空间。需要常驻说明文字时使用独立的 `Text` decorator：

```csharp
[SerializableProperty]
[Text("This message remains visible above the control.")]
public float authoredValue { get; set; }
```

内建集合包括 `Header`、`Text`、`Space`、`Tooltip`、`InspectorName`、`Range`、`InspectorReadOnly`、`ShowIf`、`HideIf` 和 `HelpBox`。多个条件按 AND 组合；`InspectorCondition` 支持 truthy/falsy、equal/not-equal、null/not-null，以及资源引用的 assigned/not-assigned。`HelpBox` 既能始终显示，也能由 sibling member 条件控制。它使用独立图标、语义色侧边、背景与边框表达 Info/Warning/Error 状态；`Text` 是无状态、常驻的普通说明；`Tooltip` 仅在悬停时展示。标题 description 也只作为 tooltip。

Inspector 的浮点标量以及 `Vector2/3/4`、`Rect`、Quaternion Euler 和 Transform 轴字段默认以一位小数显示，但底层值不会按显示精度舍入。双击字段会进入九位有效数字的 round-trip 文本编辑；带 `Range` 的 float 常态保持 slider，进入精确编辑时仍执行相同的上下界约束。

这些 Attribute 的使用点带 `Conditional("INNO_EDITOR")`。Authoring GameScripts 和 IDE 投影定义 `INNO_EDITOR`；Player deployment 还会按 `Authoring` 导出清单做语义擦除，移除使用点、自定义派生标注声明及 import，避免残留 Editor.Annotations 程序集引用。`SerializableProperty` 仍保留并负责运行时序列化。ImGui、metadata cache 和 drawer discovery 实现只存在于 Editor。

自定义展示 Attribute 继承 `InspectorPresentationAttribute`。对应 EditorScript 实现 `IInspectorAttributeDrawer`，并用 `[InspectorAttributeDrawer(typeof(MyAttribute))]` 注册；`Update` 可以组合 visibility、read-only、label、tooltip 和 numeric bounds，`DrawBefore`/`DrawAfter` 可以添加 decorator。Registry 使用 TypeCache generation snapshot 和稳定 priority 解析，所以 Plugin 可以热重载扩展而无需修改 Inspector 或中心 switch。

`PropertyDrawContext.DrawInlineChild` 同样进入 `SerializedPropertyRenderer.DrawInline`，不会自行 Resolve 或直接调用 Drawer。Inline 与普通属性共用 drawer resolution、readonly disabled scope、按完整 child path 去重的异常日志和错误呈现，但不创建额外 `PropertyRow`；Drawer 异常在当前 child 被消费，父属性与后续 Inspector 内容继续绘制。本次契约不改变 `SetValue` 的返回或 readonly 行为。

需要跨帧保留尚未提交的数字、Guid 或集合文本时，Drawer 使用 `TryGetTextState`、`SetTextState` 和
`ClearTextState`。状态由 `SerializedPropertyRenderer` 实例拥有，并以 inspected owner 弱引用、
完整 property path 和 drawer-local key 三层隔离；不同 Inspector/Editor Host、相同路径的两个对象
以及同一对象的两个 renderer 都不会串值。缓存只保存中立字符串，owner 消失后整组状态可自动
回收，不会通过静态字典固定 Scene 对象或 Plugin ALC。

## Feature 间 presentation 契约

`IInspectionIconProvider<TTarget>` 用于共享目标所属 feature 的图标规则，而不引入 Panel→Panel 引用。例如 FileBrowser 的 `AssetEditorModule` 实现 `IInspectionIconProvider<AssetFileEntry>`，它自己的 `AssetSelectionInspectionDrawer` 因而可以复用同一个 Asset icon registry。Inspector 组合根只依赖该接口，不引用 FileBrowser 项目。Scene/GameObject Drawer 直接注入 `EditorSettings`，通过原始完整路径读取与 Hierarchy、Asset Browser 一致的 icon object；Settings 项目不提供 icon resolver。

## Scripting API

EditorScripts 通过显式 `using InnoEditor.Inspection;` 使用裁剪后的 `InspectionDrawer`、`PropertyDrawer`、`InspectorAttributeDrawer` 与 property-edit 契约。Registry、snapshot、Activator 和各 feature 的具体 mutation adapter 不导出。
