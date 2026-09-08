using System;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorDrop(HierarchyInteractionIds.C_AREA)]
internal sealed class ReorderSceneDropHandler(
    IEditorSceneWorkspace workspace,
    SceneEdits edits)
    : EditorDrop<GameScene, HierarchySceneDropTarget>
{
    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor drop status that represents the completed operation.
    /// </returns>
    protected override EditorDropStatus Query(
        EditorDropContext<GameScene, HierarchySceneDropTarget> context)
    {
        GameScene source = context.source;
        GameScene target = context.target.scene;
        if (ReferenceEquals(source, target) || !source.isLoaded || !target.isLoaded)
            return EditorDropStatus.rejected;
        return EditorDropStatus.Accept(
            context.placement == EditorDropPlacement.After
                ? EditorDropVisual.InsertAfter
                : EditorDropVisual.InsertBefore);
    }

    /// <summary>
    /// Validates and applies the current editor drag-and-drop interaction atomically.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor drop result that represents the completed operation.
    /// </returns>
    protected override EditorDropResult Drop(
        EditorDropContext<GameScene, HierarchySceneDropTarget> context)
    {
        GameScene source = context.source;
        GameScene target = context.target.scene;
        int sourceIndex = IndexOf(workspace.scenes, source);
        int targetIndex = IndexOf(workspace.scenes, target);
        int insertionIndex = targetIndex + (context.placement == EditorDropPlacement.After ? 1 : 0);
        if (sourceIndex < insertionIndex)
            insertionIndex--;
        edits.SetSceneIndex(source, insertionIndex);
        _ = context.interactions.For(context.area, source).Select();
        return EditorDropResult.Accepted(source);
    }

    private static int IndexOf(System.Collections.Generic.IReadOnlyList<GameScene> scenes, GameScene scene)
    {
        for (int index = 0; index < scenes.Count; index++)
        {
            if (ReferenceEquals(scenes[index], scene))
                return index;
        }
        throw new InvalidOperationException("Only a loaded scene can be reordered.");
    }
}
