using System;
using System.Collections.Generic;

using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
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
                    SceneReferenceRestoreResult referenceResult =
                        SceneReferenceIndex.RestoreIncoming(data.incomingReferences);
                    if (!referenceResult.succeeded)
                    {
                        bool removed = Remove(data, scene, current);
                        if (referenceResult.statePreserved && removed)
                            return EditorHistoryResult.Failure(referenceResult.message);
                        return StateIntegrityFailure(
                            $"Scene element reference restore failed: {referenceResult.message} " +
                            (removed ? string.Empty : "The restored element could not be removed."));
                    }
                }
                else
                {
                    byte[] rollbackState = ScenePropertySerialization.CaptureProperties(current);
                    int rollbackIndex = GetIndex(data, scene, current);
                    SceneReferenceRollbackState[] rollbackReferences =
                        SceneReferenceIndex.CaptureCurrent(data.incomingReferences);
                    try
                    {
                        if (state.Length > 0)
                        {
                            SerializationPropertyRestoreResult propertyResult =
                                ScenePropertySerialization.RestoreProperties(current, state);
                            if (!IsComplete(propertyResult))
                                throw new InvalidOperationException("Scene element property restore was incomplete.");
                        }
                        SetIndex(data, scene, current, index);
                        SceneReferenceRestoreResult referenceResult =
                            SceneReferenceIndex.RestoreIncoming(data.incomingReferences);
                        if (!referenceResult.succeeded)
                        {
                            return RollbackExisting(
                                data,
                                scene,
                                current,
                                rollbackState,
                                rollbackIndex,
                                rollbackReferences,
                                referenceResult.message);
                        }
                    }
                    catch (Exception exception)
                    {
                        return RollbackExisting(
                            data,
                            scene,
                            current,
                            rollbackState,
                            rollbackIndex,
                            rollbackReferences,
                            exception.Message);
                    }
                }
            }
            else if (current is not null)
            {
                byte[] rollbackState = ScenePropertySerialization.CaptureProperties(current);
                int rollbackIndex = GetIndex(data, scene, current);
                SceneReferenceRollbackState[] rollbackReferences =
                    SceneReferenceIndex.CaptureCurrent(data.incomingReferences);
                try
                {
                    if (!Remove(data, scene, current))
                        return EditorHistoryResult.Failure($"Scene element '{data.elementId}' could not be removed.");
                }
                catch (Exception exception)
                {
                    EngineObject? remaining = IdentityManager.Get<EngineObject>(data.elementId);
                    if (remaining is { isDestroyed: false })
                        return EditorHistoryResult.Failure(exception.Message);
                    try
                    {
                        EngineObject restored = Restore(data, scene, rollbackIndex, rollbackState);
                            SceneReferenceRestoreResult references =
                            SceneReferenceIndex.RestoreCurrent(rollbackReferences);
                        if (!references.succeeded)
                            throw new InvalidOperationException(references.message);
                        _ = restored;
                    }
                    catch (Exception rollbackException)
                    {
                        return StateIntegrityFailure(
                            $"Scene element removal failed: {exception.Message} " +
                            $"Rollback failed: {rollbackException.Message}");
                    }
                    return EditorHistoryResult.Failure(exception.Message);
                }
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

    private static EditorHistoryResult RollbackExisting(
        SceneElementHistoryData data,
        GameScene scene,
        EngineObject current,
        byte[] rollbackState,
        int rollbackIndex,
        IReadOnlyList<SceneReferenceRollbackState> rollbackReferences,
        string failure)
    {
        var failures = new System.Collections.Generic.List<string>();
        try
        {
            SerializationPropertyRestoreResult propertyResult =
                ScenePropertySerialization.RestoreProperties(current, rollbackState);
            if (!IsComplete(propertyResult))
                failures.Add("property rollback was incomplete");
        }
        catch (Exception exception)
        {
            failures.Add($"property rollback: {exception.Message}");
        }
        try
        {
            SetIndex(data, scene, current, rollbackIndex);
        }
        catch (Exception exception)
        {
            failures.Add($"index rollback: {exception.Message}");
        }
        SceneReferenceRestoreResult referenceResult = SceneReferenceIndex.RestoreCurrent(rollbackReferences);
        if (!referenceResult.succeeded)
            failures.Add($"reference rollback: {referenceResult.message}");
        return failures.Count == 0
            ? EditorHistoryResult.Failure(failure)
            : StateIntegrityFailure(
                $"Scene element transition failed: {failure} Rollback failed: {string.Join("; ", failures)}");
    }

    private static bool IsComplete(SerializationPropertyRestoreResult result)
        => result.success && result.ignoredCount == 0 && result.restoredCount > 0;
}
