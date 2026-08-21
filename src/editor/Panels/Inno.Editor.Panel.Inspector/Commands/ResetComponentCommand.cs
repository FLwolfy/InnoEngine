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
        => SceneSnapshotOperation.Execute(
            context,
            $"Reset {context.target.component.GetType().Name}",
            context.target.gameObject.scene,
            () => context.target.gameObject.ResetComponent(context.target.component));
}
