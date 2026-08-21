using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Identity;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Hierarchy, version: 1)]
internal sealed class SceneHierarchyHistoryHandler : EditorHistoryHandler
{
    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            SceneHierarchyHistoryData data = SceneHierarchyHistoryData.Decode(change.payload.ReadBytes());
            GameScene? scene = IdentityManager.Get<GameScene>(data.sceneId);
            if (scene is not { isLoaded: true, isDestroyed: false })
                return EditorHistoryAvailability.Unavailable($"Scene '{data.sceneId}' is no longer loaded.");
            SceneObjectPlacement[] placements = direction == EditorHistoryDirection.Undo
                ? data.before
                : data.after;
            for (int i = 0; i < placements.Length; i++)
            {
                SceneObjectPlacement placement = placements[i];
                if (IdentityManager.Get<GameObject>(placement.objectId) is not { isRuntimeValid: true })
                {
                    return EditorHistoryAvailability.Unavailable(
                        $"GameObject '{placement.objectId}' is no longer available.");
                }
                if (placement.parentId is Guid parentId &&
                    IdentityManager.Get<GameObject>(parentId) is not { isRuntimeValid: true })
                {
                    return EditorHistoryAvailability.Unavailable(
                        $"Parent GameObject '{parentId}' is no longer available.");
                }
            }
            return EditorHistoryAvailability.Available();
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable($"Scene hierarchy history payload is invalid: {exception.Message}");
        }
    }

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            SceneHierarchyHistoryData data = SceneHierarchyHistoryData.Decode(change.payload.ReadBytes());
            SceneObjectPlacement[] destination = direction == EditorHistoryDirection.Undo
                ? data.before
                : data.after;
            SceneObjectPlacement[] rollback = Capture(destination);
            try
            {
                ApplyPlacements(destination);
            }
            catch
            {
                ApplyPlacements(rollback);
                throw;
            }
            GameObject? selected = IdentityManager.Get<GameObject>(data.selectedId);
            _ = context.interactions.For(context.interactions.focusedArea, selected).Select();
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }
    }

    private static SceneObjectPlacement[] Capture(IEnumerable<SceneObjectPlacement> placements)
        => placements.Select(static placement =>
        {
            GameObject gameObject = IdentityManager.Get<GameObject>(placement.objectId)
                ?? throw new InvalidOperationException($"GameObject '{placement.objectId}' is no longer available.");
            return new SceneObjectPlacement(
                placement.objectId,
                gameObject.transform.parent?.gameObject.identity.persistentId,
                gameObject.transform.siblingIndex);
        }).ToArray();

    private static void ApplyPlacements(IReadOnlyCollection<SceneObjectPlacement> placements)
    {
        var pending = new List<SceneObjectPlacement>(placements);
        while (pending.Count > 0)
        {
            bool progressed = false;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                SceneObjectPlacement placement = pending[i];
                GameObject gameObject = ResolveObject(placement.objectId);
                Transform? parent = placement.parentId is Guid parentId
                    ? ResolveObject(parentId).transform
                    : null;
                if (parent is not null && IsDescendantOf(parent, gameObject.transform))
                    continue;
                gameObject.transform.SetParent(parent);
                pending.RemoveAt(i);
                progressed = true;
            }
            if (!progressed)
                throw new InvalidOperationException("Scene hierarchy history would create a parent cycle.");
        }

        foreach (SceneObjectPlacement placement in placements.OrderBy(static value => value.siblingIndex))
            ResolveObject(placement.objectId).transform.SetSiblingIndex(placement.siblingIndex);
    }

    private static bool IsDescendantOf(Transform transform, Transform possibleAncestor)
    {
        for (Transform? current = transform; current is not null; current = current.parent)
        {
            if (ReferenceEquals(current, possibleAncestor))
                return true;
        }
        return false;
    }

    private static GameObject ResolveObject(Guid objectId)
        => IdentityManager.Get<GameObject>(objectId) is { isRuntimeValid: true } gameObject
            ? gameObject
            : throw new InvalidOperationException($"GameObject '{objectId}' is no longer available.");
}
