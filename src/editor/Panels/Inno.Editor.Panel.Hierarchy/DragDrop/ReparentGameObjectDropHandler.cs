using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;

namespace Inno.Editor.Panel.Hierarchy;

[EditorDrop(HierarchyAreas.Hierarchy)]
internal sealed class ReparentGameObjectDropHandler(SceneEdits edits)
    : EditorDrop<GameObject, HierarchyObjectDropTarget>
{
    protected override EditorDropStatus Query(
        EditorDropContext<GameObject, HierarchyObjectDropTarget> context)
    {
        GameObject source = context.source;
        GameObject target = context.target.gameObject;
        if (ReferenceEquals(source, target) || !source.isRuntimeValid || !target.isRuntimeValid ||
            !ReferenceEquals(source.scene, target.scene))
            return EditorDropStatus.rejected;
        return EditorDropStatus.Accept(context.placement switch
        {
            EditorDropPlacement.Before => EditorDropVisual.InsertBefore,
            EditorDropPlacement.After => EditorDropVisual.InsertAfter,
            _ => EditorDropVisual.Highlight
        });
    }

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
            () => ApplyDrop(context, sourceTransform, targetTransform),
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
