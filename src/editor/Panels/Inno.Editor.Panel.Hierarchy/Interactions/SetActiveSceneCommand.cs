using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_SET_ACTIVE_SCENE)]
[EditorMenu(HierarchyInteractionIds.C_AREA, "Set Active Scene", order: 100)]
internal sealed class SetActiveSceneCommand(IEditorSceneWorkspace workspace) : EditorAction<GameScene>
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
    protected override EditorActionState Query(EditorActionContext<GameScene> context)
        => context.target.isLoaded
            ? new EditorActionState(true, !ReferenceEquals(context.target, workspace.activeScene))
            : EditorActionState.hidden;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<GameScene> context)
        => workspace.SetActiveScene(context.target);
}
