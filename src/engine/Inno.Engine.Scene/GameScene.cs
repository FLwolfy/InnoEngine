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
    private readonly List<GameObject> m_rootObjects = [];
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
        gameObject.BindDefaultComponents();
        gameObject.name = name;
        m_rootObjects.Add(gameObject);
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
        m_rootObjects.Remove(gameObject);
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
    /// Gets root objects in their explicit hierarchy order.
    /// </summary>
    /// <returns>A snapshot detached from subsequent hierarchy mutations.</returns>
    public IReadOnlyList<GameObject> GetRootObjects()
    {
        PruneInvalidRoots();
        return m_rootObjects.ToArray();
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

    internal int GetRootSiblingIndex(GameObject gameObject)
    {
        return m_rootObjects.IndexOf(gameObject);
    }

    internal void SetRootSiblingIndex(GameObject gameObject, int siblingIndex)
    {
        int currentIndex = m_rootObjects.IndexOf(gameObject);
        if (currentIndex < 0)
        {
            throw new InvalidOperationException("Only root objects have a root sibling index.");
        }

        int clampedIndex = Math.Clamp(siblingIndex, 0, m_rootObjects.Count - 1);
        if (currentIndex == clampedIndex)
        {
            return;
        }

        m_rootObjects.RemoveAt(currentIndex);
        m_rootObjects.Insert(clampedIndex, gameObject);
    }

    internal void OnTransformParentChanged(
        Transform transform,
        Transform? previousParent,
        Transform? currentParent)
    {
        GameObject? gameObject = transform.gameObject;
        if (gameObject is null)
        {
            return;
        }

        if (previousParent is null)
        {
            m_rootObjects.Remove(gameObject);
        }

        if (currentParent is null && !m_rootObjects.Contains(gameObject))
        {
            m_rootObjects.Add(gameObject);
        }
    }

    private void RegisterDefaultSystems()
    {
        m_world.RegisterSystem(new BehaviorLifecycleSystem());
    }

    private void PruneInvalidRoots()
    {
        for (int i = m_rootObjects.Count - 1; i >= 0; i--)
        {
            GameObject gameObject = m_rootObjects[i];
            if (!gameObject.isRuntimeValid || gameObject.GetComponent<Transform>().parent is not null)
            {
                m_rootObjects.RemoveAt(i);
            }
        }
    }
}
