using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Serialization;
using Inno.Engine.Scene;

namespace Inno.Engine.Scene.Assets;

/// <summary>
/// Captures and restores individual scene-object properties while preserving scene reference identities.
/// </summary>
public static class ScenePropertySerialization
{
    /// <summary>
    /// Captures one persistent property without serializing the complete scene.
    /// </summary>
    /// <param name="target">The live scene object containing the property.</param>
    /// <param name="propertyName">The exact serialized member key.</param>
    /// <returns>Versioned neutral bytes that can be restored into the same logical object identity.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="propertyName"/> is empty or unknown.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the target is destroyed or not owned by a loaded scene.</exception>
    public static byte[] CaptureProperty(EngineObject target, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (target is not ISerializable serializable)
            throw new ArgumentException($"Scene object '{target.GetType().FullName}' is not serializable.", nameof(target));
        SceneGraphReferenceMap references = CreateReferences(ResolveScene(target));
        using (references.Enter())
            return SerializationManager.CapturePropertyData(serializable, propertyName);
    }

    /// <summary>
    /// Captures all persistent properties without serializing the complete scene.
    /// </summary>
    /// <param name="target">The live scene object whose state should be captured.</param>
    /// <returns>Versioned neutral bytes containing independently encoded properties.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the target is destroyed or not owned by a loaded scene.</exception>
    public static byte[] CaptureProperties(EngineObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is not ISerializable serializable)
            throw new ArgumentException($"Scene object '{target.GetType().FullName}' is not serializable.", nameof(target));
        SceneGraphReferenceMap references = CreateReferences(ResolveScene(target));
        using (references.Enter())
            return SerializationManager.CapturePropertiesData(serializable);
    }

    /// <summary>
    /// Restores independently captured properties into a live scene object.
    /// </summary>
    /// <param name="target">The current object generation receiving the values.</param>
    /// <param name="data">Bytes produced by <see cref="CaptureProperty"/> or <see cref="CaptureProperties"/>.</param>
    /// <param name="mode">The compatibility policy applied to matching current properties.</param>
    /// <returns>A summary of restored, ignored, and incompatible properties.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when strict restoration fails or the target is not in a loaded scene.</exception>
    public static SerializationPropertyRestoreResult RestoreProperties(
        EngineObject target,
        ReadOnlySpan<byte> data,
        SerializationPropertyRestoreMode mode = SerializationPropertyRestoreMode.Strict)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is not ISerializable serializable)
            throw new ArgumentException($"Scene object '{target.GetType().FullName}' is not serializable.", nameof(target));
        SceneGraphReferenceMap references = CreateReferences(ResolveScene(target));
        using (references.Enter())
            return SerializationManager.RestorePropertiesData(serializable, data, mode);
    }

    private static SceneGraphReferenceMap CreateReferences(GameScene scene)
    {
        var objects = new List<EngineObject>();
        IReadOnlyList<GameObject> gameObjects = scene.GetObjects();
        for (int i = 0; i < gameObjects.Count; i++)
        {
            GameObject gameObject = gameObjects[i];
            objects.Add(gameObject);
            IReadOnlyList<GameComponent> components = gameObject.GetComponents();
            for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
                objects.Add(components[componentIndex]);
        }
        IReadOnlyList<GameSystem> systems = scene.GetSystems();
        for (int i = 0; i < systems.Count; i++)
            objects.Add(systems[i]);

        var references = new SceneGraphReferenceMap(scene, objects);
        for (int i = 0; i < objects.Count; i++)
            references.Register(objects[i].identity.persistentId, objects[i]);
        return references;
    }

    private static GameScene ResolveScene(EngineObject target)
    {
        if (target.isDestroyed)
            throw new InvalidOperationException($"Destroyed scene object '{target.identity.persistentId}' cannot be serialized.");
        return target switch
        {
            GameScene scene when scene.isLoaded => scene,
            GameObject gameObject when gameObject.isRuntimeValid => gameObject.scene,
            GameComponent component when !component.isDestroyed => component.gameObject.scene,
            GameSystem system => ResolveSystemScene(system),
            _ => throw new InvalidOperationException(
                $"Scene object '{target.identity.persistentId}' is not owned by a loaded scene.")
        };
    }

    private static GameScene ResolveSystemScene(GameSystem system)
    {
        IReadOnlyList<GameScene> scenes = SceneManager.loadedScenes;
        for (int i = 0; i < scenes.Count; i++)
        {
            if (scenes[i].GetSystems().Contains(system))
                return scenes[i];
        }
        throw new InvalidOperationException(
            $"GameSystem '{system.identity.persistentId}' is not registered with a loaded scene.");
    }
}
