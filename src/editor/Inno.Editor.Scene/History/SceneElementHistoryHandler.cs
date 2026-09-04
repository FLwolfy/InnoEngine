using System;
using System.Collections.Generic;

using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Editor.Interactions;
using Inno.Scene;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Element)]
internal sealed class SceneElementHistoryHandler : EditorHistoryHandler
{
    private readonly EditorSceneWorkspace m_workspace;

    internal SceneElementHistoryHandler(EditorSceneWorkspace workspace)
    {
        m_workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="change">
    /// The neutral change payload to query or apply.
    /// </param>
    /// <param name="direction">
    /// The history direction that determines which state is applied.
    /// </param>
    /// <returns>
    /// The validated editor history availability that represents the completed operation.
    /// </returns>
    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            SceneElementHistoryData data = SceneElementHistoryData.Decode(change.payload.ReadBytes());
            GameScene? scene = m_workspace.Find<GameScene>(data.sceneId);
            if (scene is not { isLoaded: true, isDestroyed: false })
                return EditorHistoryAvailability.Unavailable($"Scene '{data.sceneId}' is no longer loaded.");
            bool shouldExist = direction == EditorHistoryDirection.Undo
                ? data.existsBefore
                : data.existsAfter;
            EngineObject? current = m_workspace.Find<EngineObject>(data.elementId);
            if (!shouldExist)
            {
                return current is { isDestroyed: false }
                    ? EditorHistoryAvailability.Available()
                    : EditorHistoryAvailability.Unavailable(
                        $"Scene element '{data.elementId}' is no longer available.");
            }
            if (current is { isDestroyed: false })
            {
                TypeRef typeRef = data.type;
                bool typeMatches = m_workspace.types.TryResolve(typeRef, out Type? resolvedType)
                    && current.GetType() == resolvedType;
                bool kindMatches = data.elementKind switch
                {
                    SceneElementKind.Component => current is GameComponent,
                    SceneElementKind.System => current is GameSystem,
                    _ => false
                };
                return typeMatches && kindMatches
                    ? EditorHistoryAvailability.Available()
                    : EditorHistoryAvailability.Unavailable(
                        $"Scene element '{data.elementId}' no longer matches stable type '{data.type.stableId}'.");
            }
            if (!m_workspace.types.TryResolve(data.type, out _))
            {
                return EditorHistoryAvailability.Unavailable(
                    $"Scene element type '{data.type.stableId}' is not loaded in the current generation.");
            }
            if (data.elementKind == SceneElementKind.Component &&
                m_workspace.Find<GameObject>(data.ownerId) is not { isRuntimeValid: true })
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

    /// <summary>
    /// Applies a validated change atomically at the caller-controlled commit point.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="change">
    /// The neutral change payload to query or apply.
    /// </param>
    /// <param name="direction">
    /// The history direction that determines which state is applied.
    /// </param>
    /// <returns>
    /// The validated editor history result that represents the completed operation.
    /// </returns>
    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            SceneElementHistoryData data = SceneElementHistoryData.Decode(change.payload.ReadBytes());
            GameScene? scene = m_workspace.Find<GameScene>(data.sceneId);
            if (scene is not { isLoaded: true, isDestroyed: false })
                return EditorHistoryResult.Failure($"Scene '{data.sceneId}' is no longer loaded.");
            bool shouldExist = direction == EditorHistoryDirection.Undo
                ? data.existsBefore
                : data.existsAfter;
            int index = direction == EditorHistoryDirection.Undo ? data.beforeIndex : data.afterIndex;
            byte[] state = direction == EditorHistoryDirection.Undo ? data.beforeState : data.afterState;
            EngineObject? current = m_workspace.Find<EngineObject>(data.elementId);
            if (shouldExist)
            {
                bool restored = current is null;
                if (current is null)
                {
                    try
                    {
                        current = Restore(data, scene, index, state);
                    }
                    catch (Exception exception)
                    {
                        EngineObject? partial = m_workspace.Find<EngineObject>(data.elementId);
                        if (partial is null || partial.isDestroyed)
                            return EditorHistoryResult.Failure(exception.Message);
                        SceneHistoryCompensationResult cleanup = SceneHistoryCompensation.Remove(
                            partial,
                            () => Remove(data, scene, partial),
                            $"Partially restored scene element '{data.elementId}'",
                            m_workspace);
                        return cleanup.statePreserved
                            ? EditorHistoryResult.Failure(JoinFailures(exception.Message, cleanup.message))
                            : StateIntegrityFailure(JoinFailures(exception.Message, cleanup.message));
                    }
                }
                if (current.isDestroyed)
                    return EditorHistoryResult.Failure($"Scene element '{data.elementId}' is destroyed.");
                if (restored)
                {
                    SceneReferenceRestoreResult referenceResult =
                        SceneReferenceIndex.RestoreIncoming(data.incomingReferences, m_workspace);
                    if (!referenceResult.succeeded)
                    {
                        SceneHistoryCompensationResult cleanup = SceneHistoryCompensation.Remove(
                            current,
                            () => Remove(data, scene, current),
                            $"Restored scene element '{data.elementId}'",
                            m_workspace);
                        string failure = JoinFailures(referenceResult.message, cleanup.message);
                        return referenceResult.statePreserved && cleanup.statePreserved
                            ? EditorHistoryResult.Failure(failure)
                            : StateIntegrityFailure(failure);
                    }
                }
                else
                {
                    byte[] rollbackState = ScenePropertySerialization.CaptureProperties(
                        current,
                        m_workspace.serialization);
                    int rollbackIndex = GetIndex(data, scene, current);
                    SceneReferenceRollbackState[] rollbackReferences =
                        SceneReferenceIndex.CaptureCurrent(data.incomingReferences, m_workspace);
                    try
                    {
                        if (state.Length > 0)
                        {
                            SerializationPropertyRestoreResult propertyResult =
                                ScenePropertySerialization.RestoreProperties(
                                    current,
                                    state,
                                    m_workspace.serialization);
                            if (!IsComplete(propertyResult))
                                throw new InvalidOperationException("Scene element property restore was incomplete.");
                        }
                        SetIndex(data, scene, current, index);
                        SceneReferenceRestoreResult referenceResult =
                            SceneReferenceIndex.RestoreIncoming(data.incomingReferences, m_workspace);
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
                byte[] rollbackState = ScenePropertySerialization.CaptureProperties(
                    current,
                    m_workspace.serialization);
                int rollbackIndex = GetIndex(data, scene, current);
                SceneReferenceRollbackState[] rollbackReferences =
                    SceneReferenceIndex.CaptureCurrent(data.incomingReferences, m_workspace);
                try
                {
                    if (!Remove(data, scene, current))
                        return EditorHistoryResult.Failure($"Scene element '{data.elementId}' could not be removed.");
                }
                catch (Exception exception)
                {
                    EngineObject? remaining = m_workspace.Find<EngineObject>(data.elementId);
                    if (remaining is { isDestroyed: false })
                        return EditorHistoryResult.Failure(exception.Message);
                    try
                    {
                        EngineObject restored = Restore(data, scene, rollbackIndex, rollbackState);
                            SceneReferenceRestoreResult references =
                            SceneReferenceIndex.RestoreCurrent(rollbackReferences, m_workspace);
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

    private EngineObject Restore(
        SceneElementHistoryData data,
        GameScene scene,
        int index,
        byte[] state)
        => data.elementKind switch
        {
            SceneElementKind.Component => SceneElementSerialization.RestoreComponent(
                m_workspace.Find<GameObject>(data.ownerId)
                ?? throw new InvalidOperationException($"Component owner '{data.ownerId}' is unavailable."),
                data.type,
                data.elementId,
                index,
                state,
                m_workspace.serialization),
            SceneElementKind.System => SceneElementSerialization.RestoreSystem(
                scene,
                data.type,
                data.elementId,
                index,
                state,
                m_workspace.serialization),
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

    private EditorHistoryResult RollbackExisting(
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
                ScenePropertySerialization.RestoreProperties(
                    current,
                    rollbackState,
                    m_workspace.serialization);
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
        SceneReferenceRestoreResult referenceResult = SceneReferenceIndex.RestoreCurrent(
            rollbackReferences,
            m_workspace);
        if (!referenceResult.succeeded)
            failures.Add($"reference rollback: {referenceResult.message}");
        return failures.Count == 0
            ? EditorHistoryResult.Failure(failure)
            : StateIntegrityFailure(
                $"Scene element transition failed: {failure} Rollback failed: {string.Join("; ", failures)}");
    }

    private static bool IsComplete(SerializationPropertyRestoreResult result)
        => result.success && result.ignoredCount == 0;

    private static string JoinFailures(string failure, string cleanup)
        => string.IsNullOrWhiteSpace(cleanup) ? failure : $"{failure} {cleanup}";
}
