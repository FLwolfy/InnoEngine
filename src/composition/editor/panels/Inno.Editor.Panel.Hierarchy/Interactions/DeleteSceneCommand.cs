using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Core.Input;
using Inno.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_DELETE_SCENE, priority: 100)]
[EditorMenu(HierarchyInteractionIds.C_AREA, "Delete", order: 400, separatorBefore: true)]
[EditorShortcut(HierarchyInteractionIds.C_AREA, KeyCode.Delete)]
internal sealed class DeleteSceneCommand(SceneEdits edits) : EditorAction<GameScene>
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
        => context.target is { isLoaded: true, isDestroyed: false }
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<GameScene> context)
    {
        if (!edits.CloseScene(context.target))
            return;
        if (context.interactions.selection.TryGet(out GameScene? selected) && ReferenceEquals(selected, context.target))
            _ = context.interactions.For(context.area).Select();
    }
}
