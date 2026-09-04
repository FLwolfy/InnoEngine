using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_CREATE_GAME_OBJECT)]
[EditorMenu(HierarchyInteractionIds.C_AREA, "Create Empty", order: 200)]
internal sealed class CreateGameObjectCommand(SceneEdits edits) : EditorAction<GameScene>
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
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<GameScene> context)
    {
        GameObject created = edits.CreateGameObject(context.target);
        EditorInteraction interaction = context.interactions.For(HierarchyInteractionIds.C_AREA, created);
        _ = interaction.Select();
        _ = interaction.Execute(HierarchyInteractionIds.C_RENAME);
    }
}
