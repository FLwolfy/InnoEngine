using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Hierarchy)]
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
            SceneObjectPlacement[] placements = direction == EditorHistoryDirection.Undo
                ? data.before
                : data.after;
            IReadOnlyDictionary<Guid, Guid> destinationScenes = placements.ToDictionary(
                static placement => placement.objectId,
                static placement => placement.sceneId);
            for (int i = 0; i < placements.Length; i++)
            {
                SceneObjectPlacement placement = placements[i];
                if (ResolveScene(placement.sceneId) is null)
                {
                    return EditorHistoryAvailability.Unavailable(
                        $"Scene '{placement.sceneId}' is no longer loaded.");
                }
                if (IdentityManager.Get<GameObject>(placement.objectId) is not { isRuntimeValid: true })
                {
                    return EditorHistoryAvailability.Unavailable(
                        $"GameObject '{placement.objectId}' is no longer available.");
                }
                GameObject? parent = null;
                if (placement.parentId is Guid parentId)
                {
                    parent = IdentityManager.Get<GameObject>(parentId);
                    if (parent is not { isRuntimeValid: true })
                    {
                        return EditorHistoryAvailability.Unavailable(
                            $"Parent GameObject '{parentId}' is no longer available.");
                    }
                }
                if (parent is not null &&
                    parent.scene.identity.persistentId != placement.sceneId &&
                    (!destinationScenes.TryGetValue(parent.identity.persistentId, out Guid parentDestination) ||
                     parentDestination != placement.sceneId))
                {
                    return EditorHistoryAvailability.Unavailable(
                        $"Parent GameObject '{parent.identity.persistentId}' does not belong to destination scene '{placement.sceneId}'.");
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
            catch (Exception exception)
            {
                try
                {
                    ApplyPlacements(rollback);
                }
                catch (Exception rollbackException)
                {
                    return StateIntegrityFailure(
                        $"Scene hierarchy update failed: {exception.Message} " +
                        $"Placement rollback failed: {rollbackException.Message}");
                }
                return EditorHistoryResult.Failure(exception.Message);
            }
            GameObject? selected = IdentityManager.Get<GameObject>(data.selectedId);
            try
            {
                _ = context.interactions.For(context.interactions.focusedArea, selected).Select();
            }
            catch (Exception exception)
            {
                Log.Error("Scene hierarchy selection notification failed: {0}", exception);
            }
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
                gameObject.scene.identity.persistentId,
                placement.objectId,
                gameObject.transform.parent?.gameObject.identity.persistentId,
                gameObject.transform.siblingIndex);
        }).ToArray();

    private static void ApplyPlacements(IReadOnlyCollection<SceneObjectPlacement> placements)
    {
        foreach (SceneObjectPlacement placement in placements.OrderBy(static placement =>
                     GetHierarchyDepth(ResolveObject(placement.objectId).transform)))
        {
            GameObject gameObject = ResolveObject(placement.objectId);
            GameScene destination = ResolveScene(placement.sceneId)
                ?? throw new InvalidOperationException($"Scene '{placement.sceneId}' is no longer loaded.");
            if (!ReferenceEquals(gameObject.scene, destination))
                SceneManager.MoveGameObjectToScene(gameObject, destination);
        }

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

    private static int GetHierarchyDepth(Transform transform)
    {
        int depth = 0;
        for (Transform? current = transform.parent; current is not null; current = current.parent)
            depth++;
        return depth;
    }

    private static GameObject ResolveObject(Guid objectId)
        => IdentityManager.Get<GameObject>(objectId) is { isRuntimeValid: true } gameObject
            ? gameObject
            : throw new InvalidOperationException($"GameObject '{objectId}' is no longer available.");

    private static GameScene? ResolveScene(Guid sceneId)
        => IdentityManager.Get<GameScene>(sceneId) is { isLoaded: true, isDestroyed: false } scene
            ? scene
            : null;
}
