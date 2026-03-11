using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Serialization;

namespace Inno.Core.ECS;

/// <summary>
/// Represents an entity in the scene. It holds an ID and allows component operations.
/// </summary>
public class GameObject : ISerializable
{
    [SerializableProperty] public Guid id { get; private set; } = Guid.NewGuid();
    [SerializableProperty] public string name { get; set; } = "GameObject";

    public Transform transform => GetComponent<Transform>()!;
    public readonly GameScene scene;

    public GameObject()
    {
        GameScene? activeScene = SceneManager.GetActiveScene();

        scene = activeScene ?? throw new InvalidOperationException("Can not attach GameObject to a null scene.");
        scene.RegisterGameObject(this);

        AddComponent<Transform>();
    }
    
    public GameObject(string name)
    {
        GameScene? activeScene = SceneManager.GetActiveScene();

        this.name = name;
        scene = activeScene ?? throw new InvalidOperationException("Can not attach GameObject to a null scene.");
        scene.RegisterGameObject(this);

        AddComponent<Transform>();
    }

    /// <summary>
    /// This constructor will not automatically add Transform.
    /// This is mainly used for scene deserialization.
    /// </summary>
    internal GameObject(GameScene scene)
    {
        this.scene = scene;
        scene.RegisterGameObject(this);
    }

    /// <summary>
    /// Adds a component of type T to this GameObject.
    /// </summary>
    public T AddComponent<T>() where T : GameComponent, new()
    {
        return scene.GetComponentManager().Add<T>(this);
    }

    /// <summary>
    /// Adds a component with given Type to this GameObject.
    /// </summary>
    public GameComponent? AddComponent(Type type)
    {
        return scene.GetComponentManager().Add(this, type);
    }

    /// <summary>
    /// Gets a component of type T from this GameObject.
    /// </summary>
    public T? GetComponent<T>() where T : GameComponent
    {
        return scene.GetComponentManager().Get<T>(id);
    }
    
    /// <summary>
    /// Gets all Game Components of this gameObject.
    /// </summary>
    public IReadOnlyList<GameComponent> GetAllComponents()
    {
        return scene.GetComponentManager().GetAll(id).ToList();
    }

    /// <summary>
    /// Checks whether the GameObject has a component of type T.
    /// </summary>
    public bool HasComponent<T>() where T : GameComponent
    {
        return scene.GetComponentManager().Has<T>(id);
    }

    /// <summary>
    /// Removes a component of type T from this GameObject.
    /// </summary>
    public void RemoveComponent<T>() where T : GameComponent
    {
        if (typeof(T) == typeof(Transform))
        {
            // TODO: Add warning here.
            return;
        }
        scene.GetComponentManager().Remove<T>(id);
    }

    /// <summary>
    /// Remove the component from this gameObject.
    /// </summary>
    public void RemoveComponent(GameComponent component)
    {
        if (component is Transform)
        {
            // TODO: Add warning here.
            return;
        }
        scene.GetComponentManager().Remove(id, component);
    }
}