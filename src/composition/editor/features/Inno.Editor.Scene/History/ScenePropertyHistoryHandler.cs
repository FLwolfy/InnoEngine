using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Inno.Core.Serialization;
using Inno.Editor.Interactions;
using Inno.Scene;

namespace Inno.Editor.Scene;

[EditorHistoryHandler(SceneHistoryKinds.Property)]
internal sealed class ScenePropertyHistoryHandler : EditorHistoryHandler
{
    private const double C_MERGE_WINDOW_SECONDS = 1.0;
    private readonly EditorSceneWorkspace m_workspace;

    internal ScenePropertyHistoryHandler(EditorSceneWorkspace workspace)
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
            ScenePropertyHistoryData data = ScenePropertyHistoryData.Decode(change.payload.ReadBytes());
            EngineObject? target = m_workspace.Find<EngineObject>(data.targetId);
            if (target is null || target.isDestroyed)
                return EditorHistoryAvailability.Unavailable($"Scene object '{data.targetId}' is no longer available.");
            if (target is not ISerializable serializable)
            {
                return EditorHistoryAvailability.Unavailable(
                    $"Scene object '{data.targetId}' is not serializable in the current generation.");
            }
            HashSet<string> currentProperties = ScenePropertySerialization
                .CapturePropertySnapshots(target, m_workspace.serialization, m_workspace.assets)
                .Select(static property => property.name)
                .ToHashSet(StringComparer.Ordinal);
            bool propertiesExist = data.deltas.All(delta => currentProperties.Contains(delta.propertyName));
            return propertiesExist
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable(
                    $"One or more properties affected by '{data.propertyName}' no longer exist on " +
                    $"scene object '{data.targetId}'.");
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable($"Scene property history payload is invalid: {exception.Message}");
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
        ScenePropertyHistoryData data;
        EngineObject? target;
        try
        {
            data = ScenePropertyHistoryData.Decode(change.payload.ReadBytes());
            target = m_workspace.Find<EngineObject>(data.targetId);
            if (target is null || target.isDestroyed)
                return EditorHistoryResult.Failure($"Scene object '{data.targetId}' is no longer available.");
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }

        byte[][] rollback;
        try
        {
            rollback = data.deltas
                .Select(delta => ScenePropertySerialization.CaptureProperty(
                    target,
                    delta.propertyName,
                    m_workspace.serialization,
                    m_workspace.assets))
                .ToArray();
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }

        try
        {
            RestoreDeltas(target, data.deltas, direction == EditorHistoryDirection.Redo);
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            try
            {
                for (int index = 0; index < rollback.Length; index++)
                    RestoreOne(target, rollback[index]);
            }
            catch (Exception rollbackException)
            {
                return StateIntegrityFailure(
                    $"Scene property restore failed: {exception.Message} Rollback failed: {rollbackException.Message}");
            }
            return EditorHistoryResult.Failure(exception.Message);
        }
    }

    private static bool IsComplete(SerializationPropertyRestoreResult result)
        => result.success && result.ignoredCount == 0 && result.restoredCount > 0;

    private void RestoreDeltas(
        EngineObject target,
        IReadOnlyList<ScenePropertyValueDelta> deltas,
        bool useAfter)
    {
        for (int index = 0; index < deltas.Count; index++)
            RestoreOne(target, useAfter ? deltas[index].after : deltas[index].before);
    }

    private void RestoreOne(EngineObject target, ReadOnlySpan<byte> data)
    {
        SerializationPropertyRestoreResult result = ScenePropertySerialization.RestoreProperties(
            target,
            data,
            m_workspace.serialization,
            m_workspace.assets);
        if (!IsComplete(result))
            throw new InvalidOperationException("The scene property restore was incomplete.");
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
            IReadOnlyDictionary<string, ScenePropertyValueDelta> previousByName = previous.deltas.ToDictionary(
                static delta => delta.propertyName,
                StringComparer.Ordinal);
            IReadOnlyDictionary<string, ScenePropertyValueDelta> currentByName = current.deltas.ToDictionary(
                static delta => delta.propertyName,
                StringComparer.Ordinal);
            string[] names = previous.deltas
                .Select(static delta => delta.propertyName)
                .Concat(current.deltas.Select(static delta => delta.propertyName))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            ScenePropertyValueDelta[] deltas = names
                .Select(name => new ScenePropertyValueDelta(
                    name,
                    previousByName.TryGetValue(name, out ScenePropertyValueDelta? previousDelta)
                        ? previousDelta.before
                        : currentByName[name].before,
                    currentByName.TryGetValue(name, out ScenePropertyValueDelta? currentDelta)
                        ? currentDelta.after
                        : previousByName[name].after))
                .Where(static delta => !delta.before.AsSpan().SequenceEqual(delta.after))
                .ToArray();
            if (deltas.Length == 0)
                return false;
            var data = new ScenePropertyHistoryData(
                previous.targetId,
                previous.propertyName,
                deltas,
                current.timestamp);
            merged = new EditorHistoryChange(
                SceneHistoryKinds.Property,
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
