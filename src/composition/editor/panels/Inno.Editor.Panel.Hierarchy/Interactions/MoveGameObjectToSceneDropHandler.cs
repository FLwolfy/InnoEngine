using System;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorDrop(HierarchyInteractionIds.C_AREA)]
internal sealed class MoveGameObjectToSceneDropHandler(SceneEdits edits)
    : EditorDrop<GameObject, HierarchySceneDropTarget>
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
        EditorDropContext<GameObject, HierarchySceneDropTarget> context)
    {
        GameObject source = context.source;
        GameScene target = context.target.scene;
        return source.isRuntimeValid && target is { isLoaded: true, isDestroyed: false }
            ? EditorDropStatus.Accept()
            : EditorDropStatus.rejected;
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
        EditorDropContext<GameObject, HierarchySceneDropTarget> context)
    {
        GameObject source = context.source;
        GameScene target = context.target.scene;
        _ = edits.ChangeHierarchy(
            source,
            hierarchy =>
            {
                if (!ReferenceEquals(source.scene, target))
                    hierarchy.MoveToScene(source, target);
                else
                    source.transform.SetParent(null);
                source.transform.SetSiblingIndex(GetRootCount(target) - 1);
            });
        _ = context.interactions.For(context.area, source).Select();
        return EditorDropResult.Accepted(source, target);
    }

    private static int GetRootCount(GameScene scene)
    {
        int count = 0;
        foreach (GameObject gameObject in scene.GetObjects())
        {
            if (gameObject.transform.parent is null)
                count++;
        }
        return count;
    }
}
