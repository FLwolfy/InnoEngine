# Inno.Editor.Panel.Inspector

[Editor 索引](README.md) · [Hierarchy](Inno.Editor.Panel.Hierarchy.md) · [ImGui](Inno.Editor.ImGui.md)

该项目拥有 Inspector Panel、Inspector/Property Drawer Registry、serialized property renderer、Component/System 操作、动态 Add 菜单与引用拖放。

## Registry 扩展

```csharp
[InspectorDrawer(typeof(AnimationController))]
public sealed class AnimationControllerInspector : IInspectorDrawer
{
    public void Draw(InspectorDrawContext context)
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

## 引用拖放

Asset reference handler 接受共享 `AssetInfo`；EngineObject handler 接受当前 Scene 中的 `EngineObject`。Drawer 只提交 property target 和 area，具体兼容检查及赋值在 typed Drop handler 中完成。

## Scripting API

EditorScripts 使用 `InnoEditor.Inspection`，可声明 InspectorDrawer、PropertyDrawer 并使用 draw context。具体内建 Panel、Registry snapshot 和内部 metadata cache 不导出。
