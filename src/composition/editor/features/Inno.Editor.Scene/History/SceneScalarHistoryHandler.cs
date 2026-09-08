using System;
using System.Diagnostics;

using Inno.Editor.Interactions;
using Inno.Scene;
using Inno.Scene.Layers;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Scalar)]
internal sealed class SceneScalarHistoryHandler : EditorHistoryHandler
{
    private const double C_MERGE_WINDOW_SECONDS = 1.0;
    private readonly EditorSceneWorkspace m_workspace;

    internal SceneScalarHistoryHandler(EditorSceneWorkspace workspace)
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
            SceneScalarHistoryData data = SceneScalarHistoryData.Decode(change.payload.ReadBytes());
            return Resolve(data) is not null
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable(
                    $"Scene target '{data.targetId}' is no longer available for '{data.scalarKind}'.");
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable($"Scene scalar history payload is invalid: {exception.Message}");
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
            SceneScalarHistoryData data = SceneScalarHistoryData.Decode(change.payload.ReadBytes());
            EngineObject target = Resolve(data)
                ?? throw new InvalidOperationException($"Scene target '{data.targetId}' is no longer available.");
            string rollback = GetValue(target, data.scalarKind);
            string value = direction == EditorHistoryDirection.Undo ? data.before : data.after;
            try
            {
                SetValue(target, data.scalarKind, value);
            }
            catch (Exception exception)
            {
                try
                {
                    SetValue(target, data.scalarKind, rollback);
                }
                catch (Exception rollbackException)
                {
                    return StateIntegrityFailure(
                        $"Scene scalar update failed: {exception.Message} Rollback failed: {rollbackException.Message}");
                }
                return EditorHistoryResult.Failure(exception.Message);
            }
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }
    }

    /// <summary>
    /// Attempts to merge without changing state when the operation cannot complete.
    /// </summary>
    /// <param name="older">
    /// The earlier history payload considered for coalescing.
    /// </param>
    /// <param name="newer">
    /// The later history payload considered for coalescing.
    /// </param>
    /// <param name="merged">
    /// Receives the neutral coalesced payload when merging succeeds.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    protected override bool TryMerge(
        EditorHistoryChange older,
        EditorHistoryChange newer,
        out EditorHistoryChange? merged)
    {
        merged = null;
        if (older.mergeKey is null || !string.Equals(older.mergeKey, newer.mergeKey, StringComparison.Ordinal))
            return false;
        try
        {
            SceneScalarHistoryData previous = SceneScalarHistoryData.Decode(older.payload.ReadBytes());
            SceneScalarHistoryData current = SceneScalarHistoryData.Decode(newer.payload.ReadBytes());
            if (previous.targetId != current.targetId ||
                previous.scalarKind != current.scalarKind ||
                Stopwatch.GetElapsedTime(previous.timestamp, current.timestamp).TotalSeconds > C_MERGE_WINDOW_SECONDS)
            {
                return false;
            }
            var data = new SceneScalarHistoryData(
                previous.targetId,
                previous.scalarKind,
                previous.before,
                current.after,
                current.timestamp);
            merged = new EditorHistoryChange(
                SceneHistoryKinds.Scalar,
                EditorHistoryPayload.FromBytes(data.Encode()),
                older.mergeKey);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private EngineObject? Resolve(SceneScalarHistoryData data)
        => data.scalarKind switch
        {
            SceneScalarKind.SceneName => m_workspace.Find<GameScene>(data.targetId) is
                { isLoaded: true, isDestroyed: false } scene ? scene : null,
            SceneScalarKind.GameObjectName or
                SceneScalarKind.GameObjectActive or
                SceneScalarKind.GameObjectTag or
                SceneScalarKind.GameObjectLayer =>
                m_workspace.Find<GameObject>(data.targetId) is { isRuntimeValid: true } gameObject
                    ? gameObject
                    : null,
            _ => null
        };

    private static string GetValue(EngineObject target, SceneScalarKind kind)
        => kind switch
        {
            SceneScalarKind.SceneName => ((GameScene)target).name,
            SceneScalarKind.GameObjectName => ((GameObject)target).name,
            SceneScalarKind.GameObjectActive => ((GameObject)target).activeSelf ? "1" : "0",
            SceneScalarKind.GameObjectTag => ((GameObject)target).tag,
            SceneScalarKind.GameObjectLayer => ((GameObject)target).layer.index.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Unsupported scene scalar '{kind}'.")
        };

    private static void SetValue(EngineObject target, SceneScalarKind kind, string value)
    {
        switch (kind)
        {
            case SceneScalarKind.SceneName:
                ((GameScene)target).name = value;
                break;
            case SceneScalarKind.GameObjectName:
                ((GameObject)target).name = value;
                break;
            case SceneScalarKind.GameObjectActive:
                ((GameObject)target).SetActive(string.Equals(value, "1", StringComparison.Ordinal));
                break;
            case SceneScalarKind.GameObjectTag:
                ((GameObject)target).tag = value;
                break;
            case SceneScalarKind.GameObjectLayer:
                ((GameObject)target).layer = new GameLayer(
                    int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            default:
                throw new InvalidOperationException($"Unsupported scene scalar '{kind}'.");
        }
    }
}
