using System;

using Inno.Core.Identity;
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
            SceneManager.SetSceneIndex(
                scene,
                direction == EditorHistoryDirection.Undo ? data.beforeIndex : data.afterIndex);
            _ = context.interactions.For(context.interactions.focusedArea, scene).Select();
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }
    }
}
