using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Editor.ImGui;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

internal sealed class HierarchyDragDrop
{
    private readonly HierarchySelection m_selection;

    internal HierarchyDragDrop(HierarchySelection selection)
    {
        m_selection = selection;
    }

    internal GameObject? ApplyDrop(
        GameScene scene,
        Guid droppedId,
        GameObject target,
        in TreeNodeResult result,
        HashSet<Guid> forceOpenIds)
    {
        GameObject? dropped = IdentityManager.Get<GameObject>(droppedId);
        if (dropped is null || ReferenceEquals(dropped, target) ||
            !dropped.isRuntimeValid || !ReferenceEquals(dropped.scene, scene))
            return null;

        try
        {
            Transform droppedTransform = dropped.GetComponent<Transform>();
            Transform targetTransform = target.GetComponent<Transform>();
            if (IsDescendantOf(targetTransform, droppedTransform))
                PromoteDirectChildren(droppedTransform);
            float height = MathF.Max(1f, result.max.Y - result.min.Y);
            float relativeY = (NativeImGui.GetMousePos().Y - result.min.Y) / height;
            if (relativeY is >= 0.25f and <= 0.75f)
            {
                droppedTransform.SetParent(targetTransform);
                droppedTransform.SetSiblingIndex(targetTransform.children.Count - 1);
                forceOpenIds.Add(target.identity.persistentId);
                return dropped;
            }

            Transform? targetParent = targetTransform.parent;
            droppedTransform.SetParent(targetParent);
            int sourceIndex = droppedTransform.siblingIndex;
            int targetIndex = targetTransform.siblingIndex;
            if (sourceIndex < targetIndex)
                targetIndex--;
            droppedTransform.SetSiblingIndex(targetIndex + (relativeY > 0.75f ? 1 : 0));
            return dropped;
        }
        catch (InvalidOperationException exception)
        {
            Log.Warn("Hierarchy drop was rejected: {0}", exception.Message);
            return null;
        }
    }

    internal GameObject? MoveToSceneRoot(GameScene scene, Guid droppedId)
    {
        GameObject? dropped = IdentityManager.Get<GameObject>(droppedId);
        if (dropped is null || !dropped.isRuntimeValid || !ReferenceEquals(dropped.scene, scene))
            return null;

        try
        {
            Transform transform = dropped.GetComponent<Transform>();
            transform.SetParent(null);
            transform.SetSiblingIndex(m_selection.GetRootObjects(scene).Count - 1);
            return dropped;
        }
        catch (InvalidOperationException exception)
        {
            Log.Warn("Hierarchy scene root drop was rejected: {0}", exception.Message);
            return null;
        }
    }

    internal void DrawDropPreview(in TreeNodeResult result)
    {
        float height = MathF.Max(1f, result.max.Y - result.min.Y);
        float relativeY = (NativeImGui.GetMousePos().Y - result.min.Y) / height;
        if (relativeY < 0.25f)
            ImGuiWidget.InsertionLine(result.min.X, result.max.X, result.min.Y);
        else if (relativeY > 0.75f)
            ImGuiWidget.InsertionLine(result.min.X, result.max.X, result.max.Y);
        else
        {
            Vector2 highlightMin = result.contentMin;
            highlightMin.X = MathF.Max(result.min.X, highlightMin.X - NativeImGui.GetStyle().ItemInnerSpacing.X);
            ImGuiWidget.DropTargetHighlight(highlightMin, result.max);
        }
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
