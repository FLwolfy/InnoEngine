using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorInteractionIds.C_RESET_SYSTEM, priority: 100)]
[EditorMenu(InspectorInteractionIds.C_SYSTEM_AREA, "Reset System", order: 100)]
internal sealed class ResetSystemCommand(SceneEdits edits) : EditorAction<SystemEditorTarget>
{
    protected override EditorActionState Query(EditorActionContext<SystemEditorTarget> context)
        => !context.target.system.isDestroyed && context.target.system is not MissingGameSystem
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<SystemEditorTarget> context)
        => edits.ResetSystem(context.target.scene, context.target.system);
}
