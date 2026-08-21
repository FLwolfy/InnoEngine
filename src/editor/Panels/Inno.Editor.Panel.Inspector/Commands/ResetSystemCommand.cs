using Inno.Editor.Interactions;
using Inno.Editor.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorActions.ResetSystem, priority: 100)]
[EditorMenu(InspectorAreas.System, "Reset System", order: 100)]
internal sealed class ResetSystemCommand(SceneEdits edits) : EditorAction<SystemEditorTarget>
{
    protected override EditorActionState Query(EditorActionContext<SystemEditorTarget> context)
        => !context.target.system.isDestroyed
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<SystemEditorTarget> context)
        => edits.ResetSystem(context.target.scene, context.target.system);
}
