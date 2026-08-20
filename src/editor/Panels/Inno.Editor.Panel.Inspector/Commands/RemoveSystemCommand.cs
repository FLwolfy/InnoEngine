using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorActions.RemoveSystem, priority: 100)]
[EditorMenu(InspectorAreas.System, "Remove System", order: 200)]
internal sealed class RemoveSystemCommand : EditorAction<SystemEditorTarget>
{
    protected override EditorActionState Query(EditorActionContext<SystemEditorTarget> context)
        => !context.target.system.isDestroyed
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<SystemEditorTarget> context)
        => _ = context.target.scene.RemoveSystem(context.target.system);
}
