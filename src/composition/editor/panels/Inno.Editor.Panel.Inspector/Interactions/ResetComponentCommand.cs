using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Scene;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorInteractionIds.C_RESET_COMPONENT, priority: 100)]
[EditorMenu(InspectorInteractionIds.C_COMPONENT_AREA, "Reset Component", order: 100)]
internal sealed class ResetComponentCommand(SceneEdits edits) : EditorAction<ComponentEditorTarget>
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
    protected override EditorActionState Query(EditorActionContext<ComponentEditorTarget> context)
        => !context.target.component.isDestroyed && context.target.component is not MissingGameComponent
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<ComponentEditorTarget> context)
        => edits.ResetComponent(context.target.component);
}
