# Inno.Editor.Panel.Inspector

[Editor 索引](README.md) · [Hierarchy](Inno.Editor.Panel.Hierarchy.md) · [ImGui](Inno.Editor.ImGui.md)

该项目拥有 Inspector Panel、Inspector/Property Drawer Registry、serialized property renderer、Component/System 操作、动态 Add 菜单与引用拖放。

Inspector 为所有可检查目标统一绘制无外部缝隙的 Target Header。Header 的大图标、名称、名称修改能力和第二行内容全部由当前 `InspectorDrawer<TTarget>` 提供；统一容器只负责布局、裁剪、边框和锁定。名称没有 setter 时直接显示为文字，不绘制输入框。第二行严格限制为一行，适合放置 active、tag、路径、标签或其他轻量目标信息。

Target Header 右上角提供 lock/unlock 控件，其交互面积、图标居中与 hover 表现和 Panel Tab Bar 的关闭 X 使用同一套 compact icon widget。锁定只固定 Inspector 当前展示目标，不修改全局 Selection；Hierarchy 和 File Browser 可以继续选择其他对象，以便把它们拖到被锁定目标的属性上。锁定的 Scene 对象被销毁时会自动解锁，不保留失效引用。

## Registry 扩展

```csharp
[InspectorDrawer(typeof(AnimationController))]
public sealed class AnimationControllerInspector
    : InspectorDrawer<AnimationController>
{
    public override string icon => ImGuiIcon.DiagramProject;

    protected override string GetName(
        InspectorDrawContext context,
        AnimationController target)
        => target.name;

    protected override Action<string>? GetNameSetter(
        InspectorDrawContext context,
        AnimationController target)
        => value => target.name = value;

    protected override void DrawHeader(
        InspectorDrawContext context,
        AnimationController target)
    {
        ImGui.TextUnformatted($"States: {target.stateCount}");
    }

    protected override void Draw(
        InspectorDrawContext context,
        AnimationController target)
    {
    }
}

[PropertyDrawer(typeof(AnimationCurve))]
public sealed class AnimationCurveDrawer : IPropertyDrawer
{
    public void Draw(PropertyDrawContext context)
    {
    }
}
```

两个 Registry 均基于 `TypeRegistry`，随 TypeCache generation 原子刷新；构造或冲突失败不会发布半成品。Property 顺序按照字段/属性在脚本中的 metadata 顺序统一排序，不再强制 fields 在 properties 前。

## Area 与 Action

| Area | 用途 |
| --- | --- |
| `InspectorAreas.Component` | Component card、Add/Reset/Remove。 |
| `InspectorAreas.System` | GameSystem card、Add/Reset/Remove。 |
| `InspectorAreas.EngineObjectReference` | EngineObject property drop。 |
| `InspectorAreas.AssetReference` | AssetObject property drop。 |

`InspectorActions` 提供 Add/Reset/Remove Component/System。Add 菜单是动态 `EditorMenuSource`，每次从当前 TypeCache 发现可用类型；无需在 Inspector 主类中增加分支。

Component card 的上/下按钮改变附加顺序，Transform 保持置顶且不可移除。GameSystem 也可以上下移动和删除，但运行顺序仍由显式 `order` 决定。`enabled=false` 时 header 与 body 使用统一 dimmed 样式，body 保持可辨识但不可编辑。

Inspector 的可序列化属性、Component/System enabled、Add、Remove、Reset 与显示顺序全部通过 `SceneEdits` 记录中立历史。属性修改只编码对应 root property；元素操作保存 Stable Type ID、persistent ID、index 和该元素的属性数据。Undo 不会销毁并重建无关 Scene 对象，连续属性编辑才允许按 property merge key 合并。

## 引用拖放

Asset reference handler 接受共享 `AssetInfo`；EngineObject handler 接受当前 Scene 中的 `EngineObject`。Drawer 只提交 property target 和 area，具体兼容检查及赋值在 typed Drop handler 中完成。兼容的 Asset payload 悬停在 property control 上时使用全局 `DragDropTarget` palette 绘制黄色目标框；不兼容 payload 不显示可接受反馈。

## Scripting API

EditorScripts 使用 `InnoEditor.Inspection`，可声明 InspectorDrawer、PropertyDrawer 并使用 draw context。具体内建 Panel、Registry snapshot 和内部 metadata cache 不导出。
