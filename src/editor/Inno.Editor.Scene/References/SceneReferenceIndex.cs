using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using Inno.Core.Serialization;
using Inno.Scene;
using Inno.Scene.Components;

namespace Inno.Editor.Scene;

internal static class SceneReferenceIndex
{
    internal static SceneIncomingReferenceState[] CaptureIncoming(
        GameObject root,
        EditorSceneWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(workspace);
        var subtreeIds = new HashSet<Guid>();
        CollectSubtreeIds(root, subtreeIds);
        var result = new List<SceneIncomingReferenceState>();
        foreach (EngineObject owner in EnumerateSceneObjects(root.scene))
        {
            if (subtreeIds.Contains(owner.identity.persistentId) || owner is not ISerializable serializable)
                continue;
            IReadOnlyList<SerializedProperty> properties = workspace.serialization.GetProperties(serializable);
            for (int i = 0; i < properties.Count; i++)
            {
                SerializedProperty property = properties[i];
                if (!property.canRead ||
                    !ContainsReference(property.GetValue(), subtreeIds, new HashSet<object>(ReferenceEqualityComparer.Instance)))
                {
                    continue;
                }
                result.Add(new SceneIncomingReferenceState(
                    owner.identity.persistentId,
                    property.name,
                    ScenePropertySerialization.CaptureProperty(
                        owner,
                        property.name,
                        workspace.serialization)));
            }
        }
        return result.ToArray();
    }

    internal static SceneIncomingReferenceState[] CaptureIncoming(
        EngineObject target,
        GameScene scene,
        EditorSceneWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(scene);
        var targetIds = new HashSet<Guid> { target.identity.persistentId };
        return CaptureIncoming(scene, targetIds, workspace);
    }

    internal static SceneReferenceRestoreResult RestoreIncoming(
        IReadOnlyList<SceneIncomingReferenceState> references,
        EditorSceneWorkspace workspace)
    {
        var rollback = new List<SceneReferenceRollbackState>(references.Count);
        for (int i = 0; i < references.Count; i++)
        {
            SceneIncomingReferenceState reference = references[i];
            EngineObject? owner = workspace.Find<EngineObject>(reference.ownerId);
            if (owner is null || owner.isDestroyed)
            {
                return Rollback(
                    rollback,
                    $"Incoming reference owner '{reference.ownerId}' is unavailable.",
                    workspace);
            }
            try
            {
                rollback.Add(CaptureRollback(owner, reference.propertyName, workspace.serialization));
                SerializationPropertyRestoreResult result = ScenePropertySerialization.RestoreProperties(
                    owner,
                    reference.data,
                    workspace.serialization,
                    SerializationPropertyRestoreMode.CollectFailures);
                if (!IsComplete(result))
                {
                    return Rollback(
                        rollback,
                        $"Incoming property '{reference.propertyName}' on '{reference.ownerId}' was not restored completely.",
                        workspace);
                }
            }
            catch (Exception exception)
            {
                return Rollback(
                    rollback,
                    $"Incoming property '{reference.propertyName}' on '{reference.ownerId}' failed: {exception.Message}",
                    workspace);
            }
        }
        return SceneReferenceRestoreResult.Success();
    }

    internal static SceneReferenceRollbackState[] CaptureCurrent(
        IReadOnlyList<SceneIncomingReferenceState> references,
        EditorSceneWorkspace workspace)
    {
        var result = new SceneReferenceRollbackState[references.Count];
        for (int i = 0; i < references.Count; i++)
        {
            SceneIncomingReferenceState reference = references[i];
            EngineObject owner = workspace.Find<EngineObject>(reference.ownerId)
                ?? throw new InvalidOperationException(
                    $"Incoming reference owner '{reference.ownerId}' is unavailable.");
            result[i] = CaptureRollback(owner, reference.propertyName, workspace.serialization);
        }
        return result;
    }

    internal static SceneReferenceRestoreResult RestoreCurrent(
        IReadOnlyList<SceneReferenceRollbackState> references,
        EditorSceneWorkspace workspace)
        => Rollback(references, failure: null, workspace);

    private static SceneReferenceRestoreResult Rollback(
        IReadOnlyList<SceneReferenceRollbackState> rollback,
        string? failure,
        EditorSceneWorkspace workspace)
    {
        var rollbackFailures = new List<string>();
        for (int i = rollback.Count - 1; i >= 0; i--)
        {
            SceneReferenceRollbackState reference = rollback[i];
            try
            {
                EngineObject owner = workspace.Find<EngineObject>(reference.ownerId)
                    ?? throw new InvalidOperationException("The rollback owner is unavailable.");
                if (reference.data is not null)
                {
                    SerializationPropertyRestoreResult result = ScenePropertySerialization.RestoreProperties(
                        owner,
                        reference.data,
                        workspace.serialization,
                        SerializationPropertyRestoreMode.Strict);
                    if (!IsComplete(result))
                        rollbackFailures.Add($"'{reference.ownerId}.{reference.propertyName}' was incomplete");
                }
                else
                {
                    ResolveProperty(owner, reference.propertyName, workspace.serialization)
                        .SetValue(reference.runtimeValue);
                }
            }
            catch (Exception exception)
            {
                rollbackFailures.Add($"'{reference.ownerId}.{reference.propertyName}': {exception.Message}");
            }
        }
        return rollbackFailures.Count == 0
            ? failure is null
                ? SceneReferenceRestoreResult.Success()
                : SceneReferenceRestoreResult.Failure(failure)
            : SceneReferenceRestoreResult.StateIntegrityLost(
                $"{failure ?? "Reference compensation failed."} " +
                $"Reference rollback failed: {string.Join("; ", rollbackFailures)}");
    }

    private static SceneReferenceRollbackState CaptureRollback(
        EngineObject owner,
        string propertyName,
        SerializationRegistry serialization)
    {
        try
        {
            return new SceneReferenceRollbackState(
                owner.identity.persistentId,
                propertyName,
                ScenePropertySerialization.CaptureProperty(owner, propertyName, serialization),
                runtimeValue: null);
        }
        catch (InvalidOperationException)
        {
            SerializedProperty property = ResolveProperty(owner, propertyName, serialization);
            return new SceneReferenceRollbackState(
                owner.identity.persistentId,
                propertyName,
                data: null,
                property.GetValue());
        }
    }

    private static SerializedProperty ResolveProperty(
        EngineObject owner,
        string propertyName,
        SerializationRegistry serialization)
    {
        if (owner is not ISerializable serializable)
            throw new InvalidOperationException($"Incoming reference owner '{owner.identity.persistentId}' is not serializable.");
        IReadOnlyList<SerializedProperty> properties = serialization.GetProperties(serializable);
        for (int i = 0; i < properties.Count; i++)
        {
            if (string.Equals(properties[i].name, propertyName, StringComparison.Ordinal))
                return properties[i];
        }
        throw new InvalidOperationException(
            $"Incoming property '{propertyName}' on '{owner.identity.persistentId}' is unavailable.");
    }

    private static bool IsComplete(SerializationPropertyRestoreResult result)
        => result.success && result.ignoredCount == 0 && result.restoredCount > 0;

    private static bool ContainsReference(
        object? value,
        IReadOnlySet<Guid> targetIds,
        ISet<object> visited)
    {
        if (value is null)
            return false;
        if (value is EngineObject engineObject)
            return !engineObject.isDestroyed && targetIds.Contains(engineObject.identity.persistentId);
        Type type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string or Type or Delegate or decimal or DateTime or Guid)
            return false;
        if (!type.IsValueType && !visited.Add(value))
            return false;
        if (value is IEnumerable enumerable)
        {
            foreach (object? item in enumerable)
            {
                if (ContainsReference(item, targetIds, visited))
                    return true;
            }
            return false;
        }

        for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            FieldInfo[] fields = current.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int i = 0; i < fields.Length; i++)
            {
                object? fieldValue;
                try
                {
                    fieldValue = fields[i].GetValue(value);
                }
                catch
                {
                    continue;
                }
                if (ContainsReference(fieldValue, targetIds, visited))
                    return true;
            }
        }
        return false;
    }

    private static SceneIncomingReferenceState[] CaptureIncoming(
        GameScene scene,
        IReadOnlySet<Guid> targetIds,
        EditorSceneWorkspace workspace)
    {
        var result = new List<SceneIncomingReferenceState>();
        foreach (EngineObject owner in EnumerateSceneObjects(scene))
        {
            if (targetIds.Contains(owner.identity.persistentId) || owner is not ISerializable serializable)
                continue;
            IReadOnlyList<SerializedProperty> properties = workspace.serialization.GetProperties(serializable);
            for (int i = 0; i < properties.Count; i++)
            {
                SerializedProperty property = properties[i];
                if (!property.canRead ||
                    !ContainsReference(property.GetValue(), targetIds, new HashSet<object>(ReferenceEqualityComparer.Instance)))
                {
                    continue;
                }
                result.Add(new SceneIncomingReferenceState(
                    owner.identity.persistentId,
                    property.name,
                    ScenePropertySerialization.CaptureProperty(
                        owner,
                        property.name,
                        workspace.serialization)));
            }
        }
        return result.ToArray();
    }

    private static IEnumerable<EngineObject> EnumerateSceneObjects(GameScene scene)
    {
        IReadOnlyList<GameObject> gameObjects = scene.GetObjects();
        for (int i = 0; i < gameObjects.Count; i++)
        {
            yield return gameObjects[i];
            IReadOnlyList<GameComponent> components = gameObjects[i].GetComponents();
            for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
                yield return components[componentIndex];
        }
        IReadOnlyList<GameSystem> systems = scene.GetSystems();
        for (int i = 0; i < systems.Count; i++)
            yield return systems[i];
    }

    private static void CollectSubtreeIds(GameObject gameObject, ISet<Guid> result)
    {
        _ = result.Add(gameObject.identity.persistentId);
        IReadOnlyList<GameComponent> components = gameObject.GetComponents();
        for (int i = 0; i < components.Count; i++)
            _ = result.Add(components[i].identity.persistentId);
        IReadOnlyList<Transform> children = gameObject.transform.children;
        for (int i = 0; i < children.Count; i++)
            CollectSubtreeIds(children[i].gameObject, result);
    }
}

internal readonly record struct SceneReferenceRollbackState(
    Guid ownerId,
    string propertyName,
    byte[]? data,
    object? runtimeValue);

internal readonly record struct SceneReferenceRestoreResult(
    bool succeeded,
    bool statePreserved,
    string message)
{
    internal static SceneReferenceRestoreResult Success() => new(true, true, string.Empty);

    internal static SceneReferenceRestoreResult Failure(string message) => new(false, true, message);

    internal static SceneReferenceRestoreResult StateIntegrityLost(string message) => new(false, false, message);
}
