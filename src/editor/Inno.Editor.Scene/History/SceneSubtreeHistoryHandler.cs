using System;

using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;
using Inno.Engine.Scene.Components;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Subtree)]
internal sealed class SceneSubtreeHistoryHandler : EditorHistoryHandler
{
    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            SceneSubtreeHistoryData data = SceneSubtreeHistoryData.Decode(change.payload.ReadBytes());
            GameScene? scene = IdentityManager.Get<GameScene>(data.sceneId);
            if (scene is not { isLoaded: true, isDestroyed: false })
                return EditorHistoryAvailability.Unavailable($"Scene '{data.sceneId}' is no longer loaded.");
            bool shouldExist = direction == EditorHistoryDirection.Undo
                ? data.existsBefore
                : data.existsAfter;
            GameObject? current = IdentityManager.Get<GameObject>(data.rootId);
            if (!shouldExist)
            {
                return current is { isRuntimeValid: true }
                    ? EditorHistoryAvailability.Available()
                    : EditorHistoryAvailability.Unavailable($"GameObject '{data.rootId}' is no longer available.");
            }
            if (current is { isRuntimeValid: true })
                return EditorHistoryAvailability.Available();
            if (data.parentId is Guid parentId &&
                IdentityManager.Get<GameObject>(parentId) is not { isRuntimeValid: true })
            {
                return EditorHistoryAvailability.Unavailable(
                    $"Parent GameObject '{parentId}' is no longer available.");
            }
            return EditorHistoryAvailability.Available();
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable($"Scene subtree history payload is invalid: {exception.Message}");
        }
    }

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            SceneSubtreeHistoryData data = SceneSubtreeHistoryData.Decode(change.payload.ReadBytes());
            GameScene? scene = IdentityManager.Get<GameScene>(data.sceneId);
            if (scene is not { isLoaded: true, isDestroyed: false })
                return EditorHistoryResult.Failure($"Scene '{data.sceneId}' is no longer loaded.");
            bool shouldExist = direction == EditorHistoryDirection.Undo
                ? data.existsBefore
                : data.existsAfter;
            Guid? selected = direction == EditorHistoryDirection.Undo
                ? data.selectedBefore
                : data.selectedAfter;
            GameObject? root = IdentityManager.Get<GameObject>(data.rootId);
            if (shouldExist && root is null)
            {
                Transform? parent = data.parentId is Guid parentId
                    ? IdentityManager.Get<GameObject>(parentId)?.transform
                    : null;
                try
                {
                    root = SceneSubtreeSerialization.Restore(scene, data.subtree, parent, data.siblingIndex);
                }
                catch (Exception exception)
                {
                    GameObject? partial = IdentityManager.Get<GameObject>(data.rootId);
                    if (partial is not { isRuntimeValid: true })
                        return EditorHistoryResult.Failure(exception.Message);
                    SceneHistoryCompensationResult cleanup = SceneHistoryCompensation.Remove(
                        partial,
                        () => scene.DestroyObject(partial),
                        $"Partial scene subtree '{data.rootId}'");
                    return cleanup.statePreserved
                        ? EditorHistoryResult.Failure(JoinFailures(exception.Message, cleanup.message))
                        : StateIntegrityFailure(JoinFailures(exception.Message, cleanup.message));
                }
                SceneReferenceRestoreResult referenceResult =
                    SceneReferenceIndex.RestoreIncoming(data.incomingReferences);
                if (!referenceResult.succeeded)
                {
                    SceneHistoryCompensationResult cleanup = SceneHistoryCompensation.Remove(
                        root,
                        () => scene.DestroyObject(root),
                        $"Restored scene subtree '{data.rootId}'");
                    string failure = JoinFailures(referenceResult.message, cleanup.message);
                    return referenceResult.statePreserved && cleanup.statePreserved
                        ? EditorHistoryResult.Failure(failure)
                        : StateIntegrityFailure(failure);
                }
            }
            else if (!shouldExist && root is { isRuntimeValid: true })
            {
                try
                {
                    if (!scene.DestroyObject(root))
                        return EditorHistoryResult.Failure($"GameObject '{data.rootId}' could not be removed.");
                }
                catch (Exception exception)
                {
                    if (root.isRuntimeValid)
                        return EditorHistoryResult.Failure(exception.Message);
                    try
                    {
                        Transform? parent = data.parentId is Guid parentId
                            ? IdentityManager.Get<GameObject>(parentId)?.transform
                            : null;
                        _ = SceneSubtreeSerialization.Restore(scene, data.subtree, parent, data.siblingIndex);
                        SceneReferenceRestoreResult referenceRollback =
                            SceneReferenceIndex.RestoreIncoming(data.incomingReferences);
                        if (!referenceRollback.succeeded)
                            throw new InvalidOperationException(referenceRollback.message);
                    }
                    catch (Exception rollbackException)
                    {
                        return StateIntegrityFailure(
                            $"Scene subtree removal failed: {exception.Message} " +
                            $"Rollback failed: {rollbackException.Message}");
                    }
                    return EditorHistoryResult.Failure(exception.Message);
                }
                root = null;
            }
            object? selection = selected is Guid selectionId
                ? IdentityManager.Get<EngineObject>(selectionId)
                : null;
            try
            {
                _ = context.interactions.For(context.interactions.focusedArea, selection).Select();
            }
            catch (Exception exception)
            {
                Log.Error("Scene subtree selection notification failed: {0}", exception);
            }
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }
    }

    private static string JoinFailures(string failure, string cleanup)
        => string.IsNullOrWhiteSpace(cleanup) ? failure : $"{failure} {cleanup}";
}
