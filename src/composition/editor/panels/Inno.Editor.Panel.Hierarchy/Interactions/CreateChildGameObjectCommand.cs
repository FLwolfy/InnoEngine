using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_CREATE_CHILD)]
[EditorMenu(HierarchyInteractionIds.C_AREA, "Create Empty Child", order: 100)]
internal sealed class CreateChildGameObjectCommand(SceneEdits edits) : EditorAction<GameObject>
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
    protected override EditorActionState Query(EditorActionContext<GameObject> context)
        => context.target.isRuntimeValid
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<GameObject> context)
    {
        GameObject child = edits.CreateGameObject(context.target.scene, context.target.transform);
        EditorInteraction interaction = context.interactions.For(HierarchyInteractionIds.C_AREA, child);
        _ = interaction.Select();
        _ = interaction.Execute(HierarchyInteractionIds.C_RENAME);
    }
}
