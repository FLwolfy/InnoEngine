using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Scene;
using Inno.Scene.Components;

namespace Inno.Editor.Panel.Hierarchy;

[EditorDrop(HierarchyInteractionIds.C_AREA)]
internal sealed class ReparentGameObjectDropHandler(SceneEdits edits)
    : EditorDrop<GameObject, HierarchyObjectDropTarget>
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
        EditorDropContext<GameObject, HierarchyObjectDropTarget> context)
    {
        GameObject source = context.source;
        GameObject target = context.target.gameObject;
        if (ReferenceEquals(source, target) || !source.isRuntimeValid || !target.isRuntimeValid)
            return EditorDropStatus.rejected;
        return EditorDropStatus.Accept(context.placement switch
        {
            EditorDropPlacement.Before => EditorDropVisual.InsertBefore,
            EditorDropPlacement.After => EditorDropVisual.InsertAfter,
            _ => EditorDropVisual.Highlight
        });
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
        EditorDropContext<GameObject, HierarchyObjectDropTarget> context)
    {
        GameObject source = context.source;
        GameObject target = context.target.gameObject;
        Transform sourceTransform = source.transform;
        Transform targetTransform = target.transform;
        GameObject[] relatedObjects = sourceTransform.children
            .Select(static child => child.gameObject)
            .ToArray();
        _ = edits.ChangeHierarchy(
            source,
            hierarchy =>
            {
                if (!ReferenceEquals(source.scene, target.scene))
                    hierarchy.MoveToScene(source, target.scene);
                ApplyDrop(context, sourceTransform, targetTransform);
            },
            "Reparent GameObject",
            relatedObjects);
        _ = context.interactions.For(context.area, source).Select();
        return EditorDropResult.Accepted(source, target);
    }

    private static void ApplyDrop(
        EditorDropContext<GameObject, HierarchyObjectDropTarget> context,
        Transform sourceTransform,
        Transform targetTransform)
    {
        if (context.placement == EditorDropPlacement.Into)
        {
            if (IsDescendantOf(targetTransform, sourceTransform))
                PromoteDirectChildren(sourceTransform);
            sourceTransform.SetParent(targetTransform);
            sourceTransform.SetSiblingIndex(targetTransform.children.Count - 1);
            return;
        }

        Transform? targetParent = targetTransform.parent;
        sourceTransform.SetParent(targetParent);
        int sourceIndex = sourceTransform.siblingIndex;
        int targetIndex = targetTransform.siblingIndex;
        if (sourceIndex < targetIndex)
            targetIndex--;
        sourceTransform.SetSiblingIndex(
            targetIndex + (context.placement == EditorDropPlacement.After ? 1 : 0));
    }

    private static bool IsDescendantOf(Transform transform, Transform possibleAncestor)
    {
        for (Transform? current = transform.parent; current is not null; current = current.parent)
        {
            if (ReferenceEquals(current, possibleAncestor))
                return true;
        }
        return false;
    }

    private static void PromoteDirectChildren(Transform transform)
    {
        Transform? previousParent = transform.parent;
        int insertionIndex = transform.siblingIndex;
        List<Transform> children = new(transform.children);
        for (int i = 0; i < children.Count; i++)
        {
            Transform child = children[i];
            child.SetParent(previousParent);
            child.SetSiblingIndex(insertionIndex + i);
        }
    }
}
