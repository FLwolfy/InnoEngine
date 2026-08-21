using System;

using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Element)]
internal sealed class SceneElementHistoryHandler : EditorHistoryHandler
{
    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            SceneElementHistoryData data = SceneElementHistoryData.Decode(change.payload.ReadBytes());
            GameScene? scene = IdentityManager.Get<GameScene>(data.sceneId);
            if (scene is not { isLoaded: true, isDestroyed: false })
                return EditorHistoryAvailability.Unavailable($"Scene '{data.sceneId}' is no longer loaded.");
            bool shouldExist = direction == EditorHistoryDirection.Undo
                ? data.existsBefore
                : data.existsAfter;
            EngineObject? current = IdentityManager.Get<EngineObject>(data.elementId);
            if (!shouldExist)
            {
                return current is { isDestroyed: false }
                    ? EditorHistoryAvailability.Available()
                    : EditorHistoryAvailability.Unavailable(
                        $"Scene element '{data.elementId}' is no longer available.");
            }
            if (current is { isDestroyed: false })
            {
                bool typeMatches = TypeCacheManager.TryResolveType(data.stableTypeId, out Type? currentType) &&
                                   currentType is not null && current.GetType() == currentType;
                bool kindMatches = data.elementKind switch
                {
                    SceneElementKind.Component => current is GameComponent,
                    SceneElementKind.System => current is GameSystem,
                    _ => false
                };
                return typeMatches && kindMatches
                    ? EditorHistoryAvailability.Available()
                    : EditorHistoryAvailability.Unavailable(
                        $"Scene element '{data.elementId}' no longer matches stable type '{data.stableTypeId}'.");
            }
            if (!TypeCacheManager.TryResolveType(data.stableTypeId, out Type? type) || type is null)
            {
                return EditorHistoryAvailability.Unavailable(
                    $"Scene element type '{data.stableTypeId}' is not loaded in the current generation.");
            }
            if (data.elementKind == SceneElementKind.Component &&
                IdentityManager.Get<GameObject>(data.ownerId) is not { isRuntimeValid: true })
            {
                return EditorHistoryAvailability.Unavailable(
                    $"Component owner '{data.ownerId}' is no longer available.");
            }
            return EditorHistoryAvailability.Available();
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable($"Scene element history payload is invalid: {exception.Message}");
        }
    }

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            SceneElementHistoryData data = SceneElementHistoryData.Decode(change.payload.ReadBytes());
            GameScene? scene = IdentityManager.Get<GameScene>(data.sceneId);
            if (scene is not { isLoaded: true, isDestroyed: false })
                return EditorHistoryResult.Failure($"Scene '{data.sceneId}' is no longer loaded.");
            bool shouldExist = direction == EditorHistoryDirection.Undo
                ? data.existsBefore
                : data.existsAfter;
            int index = direction == EditorHistoryDirection.Undo ? data.beforeIndex : data.afterIndex;
            byte[] state = direction == EditorHistoryDirection.Undo ? data.beforeState : data.afterState;
            EngineObject? current = IdentityManager.Get<EngineObject>(data.elementId);
            if (shouldExist)
            {
                bool restored = current is null;
                current ??= Restore(data, scene, index, state);
                if (current.isDestroyed)
                    return EditorHistoryResult.Failure($"Scene element '{data.elementId}' is destroyed.");
                if (restored)
                {
                    try
                    {
                        SceneReferenceIndex.RestoreIncoming(data.incomingReferences);
                    }
                    catch
                    {
                        _ = Remove(data, scene, current);
                        throw;
                    }
                }
                else
                {
                    byte[] rollbackState = ScenePropertySerialization.CaptureProperties(current);
                    int rollbackIndex = GetIndex(data, scene, current);
                    try
                    {
                        if (state.Length > 0)
                            _ = ScenePropertySerialization.RestoreProperties(current, state);
                        SetIndex(data, scene, current, index);
                        SceneReferenceIndex.RestoreIncoming(data.incomingReferences);
                    }
                    catch
                    {
                        _ = ScenePropertySerialization.RestoreProperties(current, rollbackState);
                        SetIndex(data, scene, current, rollbackIndex);
                        throw;
                    }
                }
            }
            else if (current is not null && !Remove(data, scene, current))
            {
                return EditorHistoryResult.Failure($"Scene element '{data.elementId}' could not be removed.");
            }
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }
    }

    private static EngineObject Restore(
        SceneElementHistoryData data,
        GameScene scene,
        int index,
        byte[] state)
        => data.elementKind switch
        {
            SceneElementKind.Component => SceneElementSerialization.RestoreComponent(
                IdentityManager.Get<GameObject>(data.ownerId)
                ?? throw new InvalidOperationException($"Component owner '{data.ownerId}' is unavailable."),
                data.stableTypeId,
                data.elementId,
                index,
                state),
            SceneElementKind.System => SceneElementSerialization.RestoreSystem(
                scene,
                data.stableTypeId,
                data.elementId,
                index,
                state),
            _ => throw new InvalidOperationException($"Unsupported scene element kind '{data.elementKind}'.")
        };

    private static bool Remove(SceneElementHistoryData data, GameScene scene, EngineObject current)
        => data.elementKind switch
        {
            SceneElementKind.Component when current is GameComponent component =>
                component.gameObject.RemoveComponent(component),
            SceneElementKind.System when current is GameSystem system => scene.RemoveSystem(system),
            _ => false
        };

    private static void SetIndex(
        SceneElementHistoryData data,
        GameScene scene,
        EngineObject current,
        int index)
    {
        if (data.elementKind == SceneElementKind.Component && current is GameComponent component)
            component.gameObject.SetComponentIndex(component, index);
        else if (data.elementKind == SceneElementKind.System && current is GameSystem system)
            scene.SetSystemIndex(system, index);
    }

    private static int GetIndex(
        SceneElementHistoryData data,
        GameScene scene,
        EngineObject current)
        => data.elementKind switch
        {
            SceneElementKind.Component when current is GameComponent component =>
                component.gameObject.GetComponentIndex(component),
            SceneElementKind.System when current is GameSystem system => scene.GetSystemIndex(system),
            _ => throw new InvalidOperationException(
                $"Scene element '{data.elementId}' does not match history kind '{data.elementKind}'.")
        };
}
