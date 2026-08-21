using System;
using System.Diagnostics;
using System.Linq;

using Inno.Core.Identity;
using Inno.Core.Serialization;
using Inno.Editor.Interactions;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Property, version: 1)]
internal sealed class ScenePropertyHistoryHandler : EditorHistoryHandler
{
    private const double C_MERGE_WINDOW_SECONDS = 1.0;

    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            ScenePropertyHistoryData data = ScenePropertyHistoryData.Decode(change.payload.ReadBytes());
            EngineObject? target = IdentityManager.Get<EngineObject>(data.targetId);
            if (target is null || target.isDestroyed)
                return EditorHistoryAvailability.Unavailable($"Scene object '{data.targetId}' is no longer available.");
            if (target is not ISerializable serializable)
            {
                return EditorHistoryAvailability.Unavailable(
                    $"Scene object '{data.targetId}' is not serializable in the current generation.");
            }
            bool propertyExists = SerializationManager.GetProperties(serializable).Any(property =>
                string.Equals(property.name, data.propertyName, StringComparison.Ordinal));
            return propertyExists
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable(
                    $"Property '{data.propertyName}' no longer exists on scene object '{data.targetId}'.");
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable($"Scene property history payload is invalid: {exception.Message}");
        }
    }

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            ScenePropertyHistoryData data = ScenePropertyHistoryData.Decode(change.payload.ReadBytes());
            EngineObject? target = IdentityManager.Get<EngineObject>(data.targetId);
            if (target is null || target.isDestroyed)
                return EditorHistoryResult.Failure($"Scene object '{data.targetId}' is no longer available.");
            byte[] rollback = ScenePropertySerialization.CaptureProperty(target, data.propertyName);
            try
            {
                ScenePropertySerialization.RestoreProperties(
                    target,
                    direction == EditorHistoryDirection.Undo ? data.before : data.after);
            }
            catch
            {
                ScenePropertySerialization.RestoreProperties(target, rollback);
                throw;
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
        if (!string.Equals(older.mergeKey, newer.mergeKey, StringComparison.Ordinal) || older.mergeKey is null)
            return false;
        try
        {
            ScenePropertyHistoryData previous = ScenePropertyHistoryData.Decode(older.payload.ReadBytes());
            ScenePropertyHistoryData current = ScenePropertyHistoryData.Decode(newer.payload.ReadBytes());
            if (previous.targetId != current.targetId ||
                !string.Equals(previous.propertyName, current.propertyName, StringComparison.Ordinal) ||
                Stopwatch.GetElapsedTime(previous.timestamp, current.timestamp).TotalSeconds > C_MERGE_WINDOW_SECONDS)
            {
                return false;
            }
            var data = new ScenePropertyHistoryData(
                previous.targetId,
                previous.propertyName,
                previous.before,
                current.after,
                current.timestamp);
            merged = new EditorHistoryChange(
                SceneHistoryKinds.Property,
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
}
