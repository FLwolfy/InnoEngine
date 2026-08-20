using Inno.Editor.Interactions.Actions;
using Inno.Editor.Interactions.Menus;
using Inno.Editor.Panel.Inspector;

namespace Inno.Editor.Panel.Inspector.Commands;

[EditorAction(InspectorActions.ResetComponent, priority: 100)]
[EditorMenu(InspectorAreas.Component, "Reset Component", order: 100)]
internal sealed class ResetComponentCommand : EditorAction<ComponentEditorTarget>
{
    protected override EditorActionState Query(EditorActionContext<ComponentEditorTarget> context)
        => !context.target.component.isDestroyed
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<ComponentEditorTarget> context)
        => context.target.gameObject.ResetComponent(context.target.component);
}
