using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using Inno.Core.Identity;
using Inno.Core.Serialization;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Assets;
using Inno.Engine.Scene.Components;

namespace Inno.Editor.Scene;

internal static class SceneReferenceIndex
{
    internal static SceneIncomingReferenceState[] CaptureIncoming(GameObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var subtreeIds = new HashSet<Guid>();
        CollectSubtreeIds(root, subtreeIds);
        var result = new List<SceneIncomingReferenceState>();
        foreach (EngineObject owner in EnumerateSceneObjects(root.scene))
        {
            if (subtreeIds.Contains(owner.identity.persistentId) || owner is not ISerializable serializable)
                continue;
            IReadOnlyList<SerializedProperty> properties = SerializationManager.GetProperties(serializable);
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
                    ScenePropertySerialization.CaptureProperty(owner, property.name)));
            }
        }
        return result.ToArray();
    }

    internal static SceneIncomingReferenceState[] CaptureIncoming(EngineObject target, GameScene scene)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(scene);
        var targetIds = new HashSet<Guid> { target.identity.persistentId };
        return CaptureIncoming(scene, targetIds);
    }

    internal static void RestoreIncoming(IReadOnlyList<SceneIncomingReferenceState> references)
    {
        for (int i = 0; i < references.Count; i++)
        {
            SceneIncomingReferenceState reference = references[i];
            EngineObject? owner = IdentityManager.Get<EngineObject>(reference.ownerId);
            if (owner is null || owner.isDestroyed)
                continue;
            _ = ScenePropertySerialization.RestoreProperties(
                owner,
                reference.data,
                SerializationPropertyRestoreMode.Compatible);
        }
    }

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
        IReadOnlySet<Guid> targetIds)
    {
        var result = new List<SceneIncomingReferenceState>();
        foreach (EngineObject owner in EnumerateSceneObjects(scene))
        {
            if (targetIds.Contains(owner.identity.persistentId) || owner is not ISerializable serializable)
                continue;
            IReadOnlyList<SerializedProperty> properties = SerializationManager.GetProperties(serializable);
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
                    ScenePropertySerialization.CaptureProperty(owner, property.name)));
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
