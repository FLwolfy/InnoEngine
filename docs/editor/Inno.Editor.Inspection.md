# Inno.Editor.Inspection

[Editor 索引](README.md) · [Inspector Panel](Inno.Editor.Panel.Inspector.md) · [Interactions](Inno.Editor.Interactions.md)

该项目是与具体 Panel 无关的检查绘制基础设施。它拥有 `InspectionDrawer<TTarget>`、`PropertyDrawer` 契约、TypeCache Registry、draw context、serialized property renderer 以及 bool、number、string、enum、collection、struct、nullable、math 等通用内建 PropertyDrawer。

`Inno.Editor.Panel.Inspector` 只负责窗口、统一 Target Header 和 Scene 的 Component/System 操作；其他 feature 不需要引用 Inspector Panel。Feature project 只需引用 `Inno.Editor.Inspection`，把自己的内部 Drawer 与目标类型放在同一个项目中，TypeCache 会自动发现它。

## InspectionDrawer

```csharp
using System;

using Inno.Editor.Inspection;
using Inno.Platform.ImGui;

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

通用 context 不包含 `SceneEdits`、AssetManager 或其他 feature service。具体 Drawer 需要领域能力时，由宿主组合根通过构造函数注入。例如 GameObject/Scene Drawer 在 Inspector Panel 内部取得 `SceneEdits`，而资产 Drawer 只取得资产 icon provider。

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

## Feature 间 presentation 契约

`IInspectionIconProvider<TTarget>` 用于共享目标所属 feature 的图标规则，而不引入 Panel→Panel 引用。例如 FileBrowser 的 `AssetEditorModule` 实现 `IInspectionIconProvider<AssetFileEntry>`，它自己的 `AssetSelectionInspectionDrawer` 因而可以复用同一个 Asset icon registry。Inspector 组合根只依赖该接口，不引用 FileBrowser 项目。Scene/GameObject Drawer 直接注入 `EditorSettings`，通过原始完整路径读取与 Hierarchy、Asset Browser 一致的 icon object；Settings 项目不提供 icon resolver。

## Scripting API

EditorScripts 通过显式 `using InnoEditor.Inspection;` 使用裁剪后的 `InspectionDrawer`、`PropertyDrawer` 与 property-edit 契约。Registry、snapshot、Activator 和各 feature 的具体 mutation adapter 不导出。
