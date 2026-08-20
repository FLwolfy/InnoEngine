using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Inspector;

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
