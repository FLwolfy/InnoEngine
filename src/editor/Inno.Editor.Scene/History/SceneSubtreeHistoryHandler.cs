using System;
using System.Collections.Generic;

using Inno.Core.Logging;
using Inno.Editor.Interactions;
using Inno.Scene;
using Inno.Scene.Components;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Subtree)]
internal sealed class SceneSubtreeHistoryHandler : EditorHistoryHandler
{
    private readonly EditorSceneWorkspace m_workspace;
    private readonly Logger m_log;

    internal SceneSubtreeHistoryHandler(EditorSceneWorkspace workspace, LogRouter logs)
    {
        m_workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        ArgumentNullException.ThrowIfNull(logs);
        m_log = logs.CreateLogger<SceneSubtreeHistoryHandler>();
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
            SceneSubtreeHistoryData data = SceneSubtreeHistoryData.Decode(change.payload.ReadBytes());
            GameScene? scene = m_workspace.Find<GameScene>(data.sceneId);
            if (scene is not { isLoaded: true, isDestroyed: false })
                return EditorHistoryAvailability.Unavailable($"Scene '{data.sceneId}' is no longer loaded.");
            bool shouldExist = direction == EditorHistoryDirection.Undo
                ? data.existsBefore
                : data.existsAfter;
            GameObject? current = m_workspace.Find<GameObject>(data.rootId);
            if (!shouldExist)
            {
                return current is { isRuntimeValid: true }
                    ? EditorHistoryAvailability.Available()
                    : EditorHistoryAvailability.Unavailable($"GameObject '{data.rootId}' is no longer available.");
            }
            if (current is { isRuntimeValid: true })
                return EditorHistoryAvailability.Available();
            if (data.parentId is Guid parentId &&
                m_workspace.Find<GameObject>(parentId) is not { isRuntimeValid: true })
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
            SceneSubtreeHistoryData data = SceneSubtreeHistoryData.Decode(change.payload.ReadBytes());
            GameScene? scene = m_workspace.Find<GameScene>(data.sceneId);
            if (scene is not { isLoaded: true, isDestroyed: false })
                return EditorHistoryResult.Failure($"Scene '{data.sceneId}' is no longer loaded.");
            bool shouldExist = direction == EditorHistoryDirection.Undo
                ? data.existsBefore
                : data.existsAfter;
            Guid? selected = direction == EditorHistoryDirection.Undo
                ? data.selectedBefore
                : data.selectedAfter;
            GameObject? root = m_workspace.Find<GameObject>(data.rootId);
            if (shouldExist && root is null)
            {
                var existing = new HashSet<GameObject>(
                    scene.GetObjects(),
                    ReferenceEqualityComparer.Instance);
                Transform? parent = data.parentId is Guid parentId
                    ? m_workspace.Find<GameObject>(parentId)?.transform
                    : null;
                try
                {
                    root = SceneSubtreeSerialization.Restore(
                        scene,
                        data.subtree,
                        m_workspace.serialization,
                        m_workspace.assets,
                        parent,
                        data.siblingIndex);
                }
                catch (Exception exception)
                {
                    SceneHistoryCompensationResult cleanup =
                        SceneHistoryCompensation.RemoveCreatedObjects(
                            scene,
                            existing,
                            $"Partial scene subtree '{data.rootId}'",
                            m_workspace);
                    return cleanup.statePreserved
                        ? EditorHistoryResult.Failure(JoinFailures(exception.Message, cleanup.message))
                        : StateIntegrityFailure(JoinFailures(exception.Message, cleanup.message));
                }
                SceneReferenceRestoreResult referenceResult =
                    SceneReferenceIndex.RestoreIncoming(data.incomingReferences, m_workspace);
                if (!referenceResult.succeeded)
                {
                    SceneHistoryCompensationResult cleanup = SceneHistoryCompensation.Remove(
                        root,
                        () => scene.DestroyObject(root),
                        $"Restored scene subtree '{data.rootId}'",
                        m_workspace);
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
                            ? m_workspace.Find<GameObject>(parentId)?.transform
                            : null;
                        _ = SceneSubtreeSerialization.Restore(
                            scene,
                            data.subtree,
                            m_workspace.serialization,
                            m_workspace.assets,
                            parent,
                            data.siblingIndex);
                        SceneReferenceRestoreResult referenceRollback =
                            SceneReferenceIndex.RestoreIncoming(data.incomingReferences, m_workspace);
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
                ? m_workspace.Find<EngineObject>(selectionId)
                : null;
            try
            {
                _ = context.interactions.For(context.interactions.focusedArea, selection).Select();
            }
            catch (Exception exception)
            {
                m_log.Write(LogLevel.Error, "Scene subtree selection notification failed: {0}", [exception]);
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
