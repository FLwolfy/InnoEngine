using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorInteractionIds.C_RESET_SYSTEM, priority: 100)]
[EditorMenu(InspectorInteractionIds.C_SYSTEM_AREA, "Reset System", order: 100)]
internal sealed class ResetSystemCommand(SceneEdits edits) : EditorAction<SystemEditorTarget>
{
    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext<SystemEditorTarget> context)
        => !context.target.system.isDestroyed && context.target.system is not MissingGameSystem
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<SystemEditorTarget> context)
        => edits.ResetSystem(context.target.scene, context.target.system);
}
