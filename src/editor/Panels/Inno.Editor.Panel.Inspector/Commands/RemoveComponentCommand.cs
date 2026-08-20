using Inno.Editor.Interactions.Actions;
using Inno.Editor.Interactions.Menus;
using Inno.Editor.Panel.Inspector;
using Inno.Engine.Scene.Components;

namespace Inno.Editor.Panel.Inspector.Commands;

[EditorAction(InspectorActions.RemoveComponent, priority: 100)]
[EditorMenu(InspectorAreas.Component, "Remove Component", order: 200)]
internal sealed class RemoveComponentCommand : EditorAction<ComponentEditorTarget>
{
    protected override EditorActionState Query(EditorActionContext<ComponentEditorTarget> context)
        => context.target.component is not Transform
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<ComponentEditorTarget> context)
    {
        if (context.target.component is not Transform)
            _ = context.target.gameObject.RemoveComponent(context.target.component);
    }
}
