using System;
using System.Collections.Generic;

using Inno.Core.ECS;
using Inno.Core.Identity;
using Inno.Engine.Scene.Components;
using Inno.Engine.Scene.Systems;

namespace Inno.Engine.Scene;

/// <summary>
/// Runtime scene backed by a single ECS world.
/// </summary>
public sealed class GameScene : IIdentityObject
{
    private readonly World m_world = new();
    private bool m_isLoaded;

    /// <summary>
    /// Gets or sets the scene display name.
    /// </summary>
    public string name { get; set; }

    /// <summary>
    /// Gets the ECS world that stores all scene data.
    /// </summary>
    public World world => m_world;

    /// <summary>
    /// Gets whether this scene is currently loaded by <see cref="SceneManager"/>.
    /// </summary>
    public bool isLoaded => m_isLoaded;

    /// <summary>
    /// Creates a scene with default systems.
    /// </summary>
    /// <param name="name">Scene display name.</param>
    public GameScene(string name = "Untitled Scene")
    {
        this.name = string.IsNullOrWhiteSpace(name) ? "Untitled Scene" : name;
        RegisterDefaultSystems();
    }

    /// <summary>
    /// Creates a new object with default scene components.
    /// </summary>
    /// <param name="name">Object display name.</param>
    /// <returns>A facade over the created entity.</returns>
    public GameObject CreateObject(string name = "GameObject")
    {
        GameObject gameObject = m_world.CreateEntity<GameObject>();
        gameObject.BindScene(this);
        m_world.AddComponent<Name>(gameObject);
        m_world.AddComponent<ActiveState>(gameObject);
        m_world.AddComponent<Transform>(gameObject);
        m_world.FlushPending();
        gameObject.name = name;
        return gameObject;
    }

    /// <summary>
    /// Destroys an object and all components stored on its entity.
    /// </summary>
    /// <param name="gameObject">Object facade to destroy.</param>
    /// <returns><see langword="true"/> when destruction was scheduled and applied.</returns>
    public bool DestroyObject(GameObject gameObject)
    {
        ArgumentNullException.ThrowIfNull(gameObject);

        if (!gameObject.isRuntimeValid || !ReferenceEquals(gameObject.scene, this))
        {
            return false;
        }

        bool killed = m_world.KillEntity(gameObject);
        m_world.FlushPending();
        return killed;
    }

    /// <summary>
    /// Gets all live objects as lightweight facades.
    /// </summary>
    /// <returns>Lazy sequence of live object facades.</returns>
    public IEnumerable<GameObject> GetObjects()
    {
        foreach (Entity entity in m_world.ViewEntitiesFast())
        {
            if (entity is GameObject gameObject)
            {
                yield return gameObject;
            }
        }
    }

    /// <summary>
    /// Finds the first live object with the requested name.
    /// </summary>
    /// <param name="name">Object name.</param>
    /// <returns>The matching object, or <see langword="null"/>.</returns>
    public GameObject? FindObject(string name)
    {
        foreach (GameObject gameObject in GetObjects())
        {
            if (string.Equals(gameObject.name, name, StringComparison.Ordinal))
            {
                return gameObject;
            }
        }

        return null;
    }

    internal void Load()
    {
        if (m_isLoaded)
        {
            return;
        }

        m_isLoaded = true;
    }

    internal void Unload()
    {
        if (!m_isLoaded)
        {
            return;
        }

        m_isLoaded = false;
    }

    internal void FixedUpdate(float fixedDeltaTime)
    {
        m_world.FixedProcess(fixedDeltaTime);
    }

    internal void Update(float deltaTime)
    {
        m_world.Process(deltaTime);
    }

    internal void LateUpdate(float deltaTime)
    {
        m_world.LateProcess(deltaTime);
    }

    internal bool ContainsEntityId(int entityId)
    {
        foreach (Entity entity in m_world.ViewEntitiesFast())
        {
            if (entity.identity.runtimeId == entityId)
            {
                return true;
            }
        }

        return false;
    }

    private void RegisterDefaultSystems()
    {
        m_world.RegisterSystem(new TransformSystem());
        m_world.RegisterSystem(new GameComponentLifecycleSystem());
    }
}
