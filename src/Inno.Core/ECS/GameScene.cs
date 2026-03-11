using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Serialization;

namespace Inno.Core.ECS;

/// <summary>
/// Represents a scene that contains and manages GameObjects.
/// </summary>
public class GameScene : ISerializable
{
    [SerializableProperty] public Guid id { get; private set; } = Guid.NewGuid();
    [SerializableProperty] public string name { get; set; } = "GameScene";
    
    private readonly List<GameObject> m_gameObjects = [];
    private readonly List<GameObject> m_pendingGameObjectRemoves = [];
    private readonly ComponentPool m_componentPool = new();
    
    private bool m_isRunning;
    private bool m_isUpdating;

    internal Camera? mainCamera;

    internal GameScene() {}
    
    internal GameScene(string name)
    {
        this.name = name;
    }
    
    /// <summary>
    /// Gets the component manager of the current game scene.
    /// </summary>
    internal ComponentPool GetComponentManager() => m_componentPool;

    internal void EndRuntime()
    {
        m_isRunning = false;
        m_isUpdating = false;
        m_componentPool.EndRuntime();
        m_pendingGameObjectRemoves.Clear();
    }
    
    // =============================
    // Serialization
    // =============================
    
    [SerializableProperty]
    private SceneSnapshot sceneSnapshot
    {
        get => SceneSnapshot.Create(in m_gameObjects, in m_componentPool);
        set
        {
            ClearForRestore();
            RestoreFromSnapshot(value);
        }
    }

    private void ClearForRestore()
    {
        mainCamera = null;
        m_pendingGameObjectRemoves.Clear();
        m_componentPool.ClearAll();
        m_gameObjects.Clear();
        m_isRunning = false;
        m_isUpdating = false;
    }

    private void RestoreFromSnapshot(SceneSnapshot snapshot)
    {
        // First pass: create all objects (so parenting can resolve).
        foreach (var goe in snapshot.gameObjectEntries)
        {
            var go = new GameObject(this);
            ((ISerializable)go).RestoreState(goe.objectState);
        }
        
        // Second pass: restore transform
        for (int i = 0; i < snapshot.gameObjectEntries.Count; i++)
        {
            // Object
            var goe = snapshot.gameObjectEntries[i];
            var go = m_gameObjects[i];

            // Transform retore
            var transState = goe.componentEntries
                .Where(ce =>
                {
                    var t = Type.GetType(ce.typeName);
                    return t != null && typeof(Transform).IsAssignableFrom(t);
                })
                .Select(ce => ce.componentState)
                .First();
            var trans = go.AddComponent<Transform>();
            ((ISerializable)trans).RestoreState(transState);
        }
        
        // Third pass: parent-child relation
        foreach (var go in m_gameObjects)
        {
            var trans = go.transform;
            var parentId = trans.parentId;
            if (parentId != Guid.Empty)
            {
                trans.SetParent(FindGameObject(parentId)!.transform, worldTransformStays: false);
            }
        }
        
        // Forth pass: restore components and state.
        for (int i = 0; i < snapshot.gameObjectEntries.Count; i++)
        {
            var goe = snapshot.gameObjectEntries[i];
            var go = m_gameObjects[i];
            foreach (var ce in goe.componentEntries)
            {
                var type = Type.GetType(ce.typeName);
                if (type == null || typeof(Transform).IsAssignableFrom(type)) continue;
                
                var comp = go.AddComponent(type);
                if (comp is ISerializable serializable)
                {
                    serializable.RestoreState(ce.componentState);
                }
            }
        }
    }
    
    // =============================
    // APIs
    // =============================

    /// <summary>
    /// Gets all components of a specific type in the current game scene.
    /// </summary>
    public IEnumerable<T> GetAllComponents<T>() where T : GameComponent
    {
        return m_componentPool.GetAll<T>();
    }

    /// <summary>
    /// Gets all components assignable to a specific type in the current game scene.
    /// </summary>
    public IEnumerable<T> GetAllComponentsAs<T>() where T : GameComponent
    {
        return m_componentPool.GetAllAssignableTo<T>();
    }
    
    /// <summary>
    /// Gets all components of a specific type in the current game scene.
    /// </summary>
    public IEnumerable<T> GetAllComponent<T>() where T : GameComponent
    {
        return m_componentPool.GetAll<T>();
    }
        
    /// <summary>
    /// Gets the main camera of the current game scene.
    /// </summary>
    public Camera? GetMainCamera() => mainCamera;

    /// <summary>
    /// Registers a GameObject to this scene.
    /// </summary>
    public void RegisterGameObject(GameObject obj)
    {
        if (m_gameObjects.Contains(obj)) { return; }
        m_gameObjects.Add(obj);
    }

    /// <summary>
    /// Unregisters a GameObject from this scene.
    /// </summary>
    public void UnregisterGameObject(GameObject obj)
    {
        var components = m_componentPool.GetAll(obj.id);
        foreach (var comp in components)
        {
            m_componentPool.Remove(obj.id, comp);
        }

        if (m_isRunning || m_isUpdating)
        {
            m_pendingGameObjectRemoves.Add(obj);
        }
        else
        {
            m_gameObjects.Remove(obj);
        }
    }

    /// <summary>
    /// Get a gameobject with its uuid.
    /// </summary>
    public GameObject? FindGameObject(Guid uid)
    {
        return m_gameObjects.Find(obj => obj.id == uid);
    }
    
    /// <summary>
    /// Get a gameobject with its name.
    /// </summary>
    public GameObject? FindGameObject(string objName)
    {
        return m_gameObjects.Find(obj => obj.name == objName);
    }

    /// <summary>
    /// Gets all GameObjects in this scene.
    /// </summary>
    public IReadOnlyList<GameObject> GetAllGameObjects()
    {
        return m_gameObjects;
    }

    /// <summary>
    /// Gets all GameObjects that have no parent in this scene.
    /// </summary>
    public IReadOnlyList<GameObject> GetAllRootGameObjects()
    {
        return m_gameObjects.Where(go => go.transform.parent == null).ToList();
    }

    /// <summary>
    /// Called when the game started.
    /// </summary>
    internal void BeginRuntime()
    {
        m_componentPool.WakeAll();
        m_componentPool.BeginRuntime();
        m_isRunning = true;
    }

    /// <summary>
    /// Update the GameScene and its gameObjects and components.
    /// </summary>
    internal void Update()
    {
        // Updates
        m_isUpdating = true;
        m_componentPool.UpdateAll();
        m_isUpdating = false;
        
        // Remove objects
        foreach (var gameObject in m_pendingGameObjectRemoves) { m_gameObjects.Remove(gameObject); }
        m_pendingGameObjectRemoves.Clear();
    }
}