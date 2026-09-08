using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Scene.Components;

namespace Inno.Editor.Panel.Inspector;

[EditorAction(InspectorInteractionIds.C_REMOVE_COMPONENT, priority: 100)]
[EditorMenu(InspectorInteractionIds.C_COMPONENT_AREA, "Remove Component", order: 200)]
internal sealed class RemoveComponentCommand(SceneEdits edits) : EditorAction<ComponentEditorTarget>
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
        => context.target.component is not Transform
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<ComponentEditorTarget> context)
    {
        if (context.target.component is not Transform)
            _ = edits.RemoveComponent(context.target.component);
    }
}
