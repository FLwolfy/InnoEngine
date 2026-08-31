using System;

using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Order)]
internal sealed class SceneOrderHistoryHandler : EditorHistoryHandler
{
    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            SceneOrderHistoryData data = SceneOrderHistoryData.Decode(change.payload.ReadBytes());
            return IdentityManager.Get<GameScene>(data.sceneId) is { isLoaded: true, isDestroyed: false }
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Scene '{data.sceneId}' is no longer loaded.");
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable($"Scene order history payload is invalid: {exception.Message}");
        }
    }

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            SceneOrderHistoryData data = SceneOrderHistoryData.Decode(change.payload.ReadBytes());
            GameScene? scene = IdentityManager.Get<GameScene>(data.sceneId);
            if (scene is not { isLoaded: true, isDestroyed: false })
                return EditorHistoryResult.Failure($"Scene '{data.sceneId}' is no longer loaded.");
            int rollbackIndex = SceneManager.GetSceneIndex(scene);
            try
            {
                SceneManager.SetSceneIndex(
                    scene,
                    direction == EditorHistoryDirection.Undo ? data.beforeIndex : data.afterIndex);
            }
            catch (Exception exception)
            {
                try
                {
                    SceneManager.SetSceneIndex(scene, rollbackIndex);
                }
                catch (Exception rollbackException)
                {
                    return StateIntegrityFailure(
                        $"Scene reorder failed: {exception.Message} Rollback failed: {rollbackException.Message}");
                }
                return EditorHistoryResult.Failure(exception.Message);
            }
            try
            {
                _ = context.interactions.For(context.interactions.focusedArea, scene).Select();
            }
            catch (Exception exception)
            {
                Log.Error("Scene order selection notification failed: {0}", exception);
            }
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }
    }
}
