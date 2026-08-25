using Inno.Editor.Interactions;
using Inno.Editor.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorInteractionIds.C_REMOVE_SYSTEM, priority: 100)]
[EditorMenu(InspectorInteractionIds.C_SYSTEM_AREA, "Remove System", order: 200)]
internal sealed class RemoveSystemCommand(SceneEdits edits) : EditorAction<SystemEditorTarget>
{
    internal static EditorCommand command { get; } = new(InspectorInteractionIds.removeSystem);

    protected override EditorActionState Query(EditorActionContext<SystemEditorTarget> context)
        => !context.target.system.isDestroyed
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<SystemEditorTarget> context)
        => _ = edits.RemoveSystem(context.target.scene, context.target.system);
}
