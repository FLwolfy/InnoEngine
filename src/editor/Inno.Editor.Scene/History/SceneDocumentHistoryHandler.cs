using System;

using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Document)]
internal sealed class SceneDocumentHistoryHandler(EditorSceneWorkspace workspace) : EditorHistoryHandler
{
    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            SceneDocumentHistoryData data = SceneDocumentHistoryData.Decode(change.payload.ReadBytes());
            bool shouldExist = direction == EditorHistoryDirection.Undo
                ? data.existsBefore
                : data.existsAfter;
            GameScene? current = IdentityManager.Get<GameScene>(data.snapshot.sceneId);
            if (shouldExist)
            {
                return current is null || current is { isLoaded: true, isDestroyed: false }
                    ? EditorHistoryAvailability.Available()
                    : EditorHistoryAvailability.Unavailable(
                        $"Scene '{data.snapshot.sceneId}' exists but is not a live loaded scene.");
            }
            if (current is not { isLoaded: true, isDestroyed: false })
            {
                return EditorHistoryAvailability.Unavailable(
                    $"Scene '{data.snapshot.sceneId}' is no longer loaded.");
            }
            return EditorHistoryAvailability.Available();
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable(
                $"Scene document history payload is invalid: {exception.Message}");
        }
    }

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        SceneDocumentHistoryData data;
        try
        {
            data = SceneDocumentHistoryData.Decode(change.payload.ReadBytes());
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }

        bool shouldExist = direction == EditorHistoryDirection.Undo
            ? data.existsBefore
            : data.existsAfter;
        Guid? active = direction == EditorHistoryDirection.Undo
            ? data.activeBefore
            : data.activeAfter;
        Guid? selected = direction == EditorHistoryDirection.Undo
            ? data.selectedBefore
            : data.selectedAfter;
        GameScene? current = IdentityManager.Get<GameScene>(data.snapshot.sceneId);
        bool existed = current is { isLoaded: true, isDestroyed: false };
        EditorSceneWorkspace.SceneDocumentSnapshot? original = existed
            ? workspace.CaptureDocumentSnapshot(current!)
            : null;
        Guid? originalActive = SceneManager.activeScene?.identity.persistentId;
        DocumentMutation mutation = DocumentMutation.None;

        try
        {
            if (shouldExist)
            {
                if (current is null)
                {
                    current = workspace.RestoreDocumentSnapshot(data.snapshot);
                    mutation = DocumentMutation.Restored;
                }
                else if (current is not { isLoaded: true, isDestroyed: false })
                {
                    return EditorHistoryResult.Failure(
                        $"Scene '{data.snapshot.sceneId}' cannot be restored over an invalid live object.");
                }
            }
            else if (current is not null)
            {
                if (!workspace.CloseDocumentForHistory(current))
                    return EditorHistoryResult.Failure($"Scene '{data.snapshot.sceneId}' could not be closed.");
                mutation = DocumentMutation.Closed;
            }

            workspace.RestoreActiveScene(active);
        }
        catch (Exception exception)
        {
            try
            {
                RollbackDocument(workspace, data.snapshot.sceneId, mutation, original);
                workspace.RestoreActiveScene(originalActive);
            }
            catch (Exception rollbackException)
            {
                return StateIntegrityFailure(
                    $"Scene document transition failed: {exception.Message} " +
                    $"Rollback failed: {rollbackException.Message}");
            }
            return EditorHistoryResult.Failure(exception.Message);
        }

        try
        {
            workspace.RestoreSelection(selected);
        }
        catch (Exception exception)
        {
            Log.Error("Scene document selection notification failed: {0}", exception);
        }
        return EditorHistoryResult.Success();
    }

    private static void RollbackDocument(
        EditorSceneWorkspace workspace,
        Guid sceneId,
        DocumentMutation mutation,
        EditorSceneWorkspace.SceneDocumentSnapshot? original)
    {
        if (mutation == DocumentMutation.Restored)
        {
            GameScene? restored = IdentityManager.Get<GameScene>(sceneId);
            if (restored is { isLoaded: true, isDestroyed: false } &&
                !workspace.CloseDocumentForHistory(restored))
            {
                throw new InvalidOperationException($"Restored scene '{sceneId}' could not be removed.");
            }
        }
        else if (mutation == DocumentMutation.Closed)
        {
            _ = workspace.RestoreDocumentSnapshot(
                original ?? throw new InvalidOperationException("The original scene document snapshot is missing."));
        }
    }

    private enum DocumentMutation
    {
        None,
        Restored,
        Closed
    }
}
