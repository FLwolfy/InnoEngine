using Inno.Editor.Interactions;
using Inno.Editor.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorActions.RemoveSystem, priority: 100)]
[EditorMenu(InspectorAreas.System, "Remove System", order: 200)]
internal sealed class RemoveSystemCommand(SceneEdits edits) : EditorAction<SystemEditorTarget>
{
    protected override EditorActionState Query(EditorActionContext<SystemEditorTarget> context)
        => !context.target.system.isDestroyed
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<SystemEditorTarget> context)
        => _ = edits.RemoveSystem(context.target.scene, context.target.system);
}
