using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorActions.ResetSystem, priority: 100)]
[EditorMenu(InspectorAreas.System, "Reset System", order: 100)]
internal sealed class ResetSystemCommand : EditorAction<SystemEditorTarget>
{
    protected override EditorActionState Query(EditorActionContext<SystemEditorTarget> context)
        => !context.target.system.isDestroyed
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<SystemEditorTarget> context)
        => SceneSnapshotOperation.Execute(
            context,
            $"Reset {context.target.system.GetType().Name}",
            context.target.scene,
            () => context.target.scene.ResetSystem(context.target.system));
}
