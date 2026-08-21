using System;

using Inno.Core.Identity;
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
                root = SceneSubtreeSerialization.Restore(scene, data.subtree, parent, data.siblingIndex);
                SceneReferenceIndex.RestoreIncoming(data.incomingReferences);
            }
            else if (!shouldExist && root is { isRuntimeValid: true })
            {
                _ = scene.DestroyObject(root);
                root = null;
            }
            object? selection = selected is Guid selectionId
                ? IdentityManager.Get<EngineObject>(selectionId)
                : null;
            _ = context.interactions.For(context.interactions.focusedArea, selection).Select();
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }
    }
}
