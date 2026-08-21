using System;
using System.Diagnostics;

using Inno.Core.Identity;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Scalar, version: 1)]
internal sealed class SceneScalarHistoryHandler : EditorHistoryHandler
{
    private const double C_MERGE_WINDOW_SECONDS = 1.0;

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
            string value = direction == EditorHistoryDirection.Undo ? data.before : data.after;
            switch (data.scalarKind)
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
                default:
                    throw new InvalidOperationException($"Unsupported scene scalar '{data.scalarKind}'.");
            }
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }
    }

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
                version: 1,
                EditorHistoryPayload.FromBytes(data.Encode()),
                older.mergeKey);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static EngineObject? Resolve(SceneScalarHistoryData data)
        => data.scalarKind switch
        {
            SceneScalarKind.SceneName => IdentityManager.Get<GameScene>(data.targetId) is
                { isLoaded: true, isDestroyed: false } scene ? scene : null,
            SceneScalarKind.GameObjectName or SceneScalarKind.GameObjectActive =>
                IdentityManager.Get<GameObject>(data.targetId) is { isRuntimeValid: true } gameObject
                    ? gameObject
                    : null,
            _ => null
        };
}
