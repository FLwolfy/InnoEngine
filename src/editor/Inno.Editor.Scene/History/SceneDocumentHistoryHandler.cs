using System;

using Inno.Core.Identity;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Document, version: 1)]
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
            return SceneManager.loadedScenes.Count > 1
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable("The final loaded scene cannot be closed.");
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
        try
        {
            SceneDocumentHistoryData data = SceneDocumentHistoryData.Decode(change.payload.ReadBytes());
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
            if (shouldExist)
            {
                if (current is null)
                    current = workspace.RestoreDocumentSnapshot(data.snapshot);
                else if (current is not { isLoaded: true, isDestroyed: false })
                    return EditorHistoryResult.Failure(
                        $"Scene '{data.snapshot.sceneId}' cannot be restored over an invalid live object.");
            }
            else if (current is not null && !workspace.CloseDocumentForHistory(current))
            {
                return EditorHistoryResult.Failure($"Scene '{data.snapshot.sceneId}' could not be closed.");
            }

            workspace.RestoreEditorState(active, selected);
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }
    }
}
