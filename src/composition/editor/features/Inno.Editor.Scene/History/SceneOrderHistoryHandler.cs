using System;

using Inno.Core.Logging;
using Inno.Editor.Interactions;
using Inno.Scene;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Order)]
internal sealed class SceneOrderHistoryHandler : EditorHistoryHandler
{
    private readonly EditorSceneWorkspace m_workspace;
    private readonly Logger m_log;

    internal SceneOrderHistoryHandler(EditorSceneWorkspace workspace, LogRouter logs)
    {
        m_workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        ArgumentNullException.ThrowIfNull(logs);
        m_log = logs.CreateLogger<SceneOrderHistoryHandler>();
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
            SceneOrderHistoryData data = SceneOrderHistoryData.Decode(change.payload.ReadBytes());
            return m_workspace.Find<GameScene>(data.sceneId) is { isLoaded: true, isDestroyed: false }
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Scene '{data.sceneId}' is no longer loaded.");
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable($"Scene order history payload is invalid: {exception.Message}");
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
            SceneOrderHistoryData data = SceneOrderHistoryData.Decode(change.payload.ReadBytes());
            GameScene? scene = m_workspace.Find<GameScene>(data.sceneId);
            if (scene is not { isLoaded: true, isDestroyed: false })
                return EditorHistoryResult.Failure($"Scene '{data.sceneId}' is no longer loaded.");
            int rollbackIndex = m_workspace.world.GetSceneIndex(scene);
            try
            {
                m_workspace.world.SetSceneIndex(
                    scene,
                    direction == EditorHistoryDirection.Undo ? data.beforeIndex : data.afterIndex);
            }
            catch (Exception exception)
            {
                try
                {
                    m_workspace.world.SetSceneIndex(scene, rollbackIndex);
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
                m_log.Write(LogLevel.Error, "Scene order selection notification failed: {0}", [exception]);
            }
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }
    }
}
