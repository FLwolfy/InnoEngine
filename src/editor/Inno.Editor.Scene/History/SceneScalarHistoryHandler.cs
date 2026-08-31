using System;
using System.Diagnostics;

using Inno.Core.Identity;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Layers;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Scalar)]
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

    private static EngineObject? Resolve(SceneScalarHistoryData data)
        => data.scalarKind switch
        {
            SceneScalarKind.SceneName => IdentityManager.Get<GameScene>(data.targetId) is
                { isLoaded: true, isDestroyed: false } scene ? scene : null,
            SceneScalarKind.GameObjectName or
                SceneScalarKind.GameObjectActive or
                SceneScalarKind.GameObjectTag or
                SceneScalarKind.GameObjectLayer =>
                IdentityManager.Get<GameObject>(data.targetId) is { isRuntimeValid: true } gameObject
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
