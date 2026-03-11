using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Core.ECS;

/// <summary>
/// Manages components by entity and updates components ordered by tag.
/// </summary>
internal class ComponentPool
{
    private bool m_isRunning;
    private bool m_isUpdating;
    
    private static readonly ComponentTag[] ORDERED_TAGS = Enum.GetValues<ComponentTag>();

    private readonly Dictionary<ComponentTag, List<GameComponent>> m_componentsByTag = new();
    private readonly Dictionary<Type, List<GameComponent>> m_componentsByType = new();
    private readonly Dictionary<Guid, Dictionary<Type, GameComponent>> m_componentsByEntity = new();
    
    private readonly List<Action> m_pendingAddRemoveAction = [];
    private readonly List<GameComponent> m_pendingStartComponents = [];
    private readonly List<List<GameComponent>> m_pendingToSortLists = [];
    
    public ComponentPool()
    {
        foreach (var tag in Enum.GetValues<ComponentTag>())
        {
            m_componentsByTag[tag] = [];
        }
    }
    
    /// <summary>
    /// Tell the manager the game is running.
    /// </summary>
    public void BeginRuntime()
    {
        m_isRunning = true;
    }

    public void EndRuntime()
    {
        m_isRunning = false;
        m_isUpdating = false;
        m_pendingAddRemoveAction.Clear();
        m_pendingStartComponents.Clear();
        m_pendingToSortLists.Clear();
    }

    public void ClearAll()
    {
        // Detach all components
        foreach (var entity in m_componentsByEntity.Values)
        {
            foreach (var comp in entity.Values)
            {
                comp.OnDetach();
            }
        }

        m_componentsByEntity.Clear();
        m_componentsByType.Clear();

        foreach (var tag in Enum.GetValues<ComponentTag>())
        {
            m_componentsByTag[tag].Clear();
        }

        EndRuntime();
    }
    
    /// <summary>
    /// Adds a component of type T to the entity if it doesn't exist.
    /// Returns the existing or new component instance.
    /// </summary>
    public T Add<T>(GameObject obj) where T : GameComponent, new()
    {
        // Initialize the List for a new entity
        if (!m_componentsByEntity.TryGetValue(obj.id, out var entityComponents))
        {
            entityComponents = new Dictionary<Type, GameComponent>();
            m_componentsByEntity[obj.id] = entityComponents;
        }

        var type = typeof(T);

        // Already exists, return it directly
        if (entityComponents.TryGetValue(type, out var existingComponent))
        {
            return (T)existingComponent;
        }

        // Create new component instance
        var component = new T();
        component.Initialize(obj);
        entityComponents[type] = component;
        AddToTypeMap(component);
        
        // Add or delay add to tag map (for safe iterations)
        if (m_isUpdating) { m_pendingAddRemoveAction.Add(() => InsertSorted(m_componentsByTag[component.orderTag], component)); }
        else { InsertSorted(m_componentsByTag[component.orderTag], component); }
        
        // Game already started, execute Awake()
        if (m_isRunning && !component.hasAwakened)
        {
            component.Awake();
            component.hasAwakened = true;
        }

        // Delay Start()
        if (component.isActive && !component.hasStarted)
        {
            m_pendingStartComponents.Add(component);
        }
        
        return component;
    }
    
    /// <summary>
    /// Adds an existing component instance to the entity if it doesn't exist.
    /// Returns the existing component (if already present) or the provided instance (if added).
    /// Initialization behavior matches other Add overloads:
    /// </summary>
    public GameComponent Add(GameObject obj, GameComponent component)
    {
        if (component == null) throw new ArgumentNullException(nameof(component));

        // Initialize the map for a new entity
        if (!m_componentsByEntity.TryGetValue(obj.id, out var entityComponents))
        {
            entityComponents = new Dictionary<Type, GameComponent>();
            m_componentsByEntity[obj.id] = entityComponents;
        }

        var type = component.GetType();

        // Already exists, return it directly (same semantics as other Add overloads)
        if (entityComponents.TryGetValue(type, out var existingComponent))
        {
            return existingComponent;
        }

        // Ensure it is a valid component type (defensive; aligns with Add(obj, Type))
        if (!typeof(GameComponent).IsAssignableFrom(type))
        {
            throw new ArgumentException($"Type {type} is not a GameComponent.", nameof(component));
        }

        // Initialize + register
        component.Initialize(obj);
        entityComponents[type] = component;

        // Type map
        AddToTypeMap(component);

        // Tag map (safe iteration)
        if (m_isUpdating)
        {
            m_pendingAddRemoveAction.Add(() => InsertSorted(m_componentsByTag[component.orderTag], component));
        }
        else
        {
            InsertSorted(m_componentsByTag[component.orderTag], component);
        }

        // Game already started, execute Awake()
        if (m_isRunning && !component.hasAwakened)
        {
            component.Awake();
            component.hasAwakened = true;
        }

        // Delay Start()
        if (component.isActive && !component.hasStarted)
        {
            m_pendingStartComponents.Add(component);
        }

        return component;
    }

    
    /// <summary>
    /// Adds a component with given type to the entity if it doesn't exist.
    /// Returns the existing or new component instance.
    /// </summary>
    public GameComponent? Add(GameObject obj, Type type)
    {
        // Check type
        if (!typeof(GameComponent).IsAssignableFrom(type)) { return null; }
        
        // Initialize the List for a new entity
        if (!m_componentsByEntity.TryGetValue(obj.id, out var entityComponents))
        {
            entityComponents = new Dictionary<Type, GameComponent>();
            m_componentsByEntity[obj.id] = entityComponents;
        }

        // Already exists, return it directly
        if (entityComponents.TryGetValue(type, out var existingComponent))
        {
            return existingComponent;
        }

        // Create new component instance
        var component = (GameComponent?)Activator.CreateInstance(type);
        if (component == null) return null;
        
        component.Initialize(obj);
        entityComponents[type] = component;
        AddToTypeMap(component);
        
        // Add or delay add to tag map (for safe iterations)
        if (m_isUpdating) { m_pendingAddRemoveAction.Add(() => InsertSorted(m_componentsByTag[component.orderTag], component)); }
        else { InsertSorted(m_componentsByTag[component.orderTag], component); }
        
        // Game already started, execute Awake()
        if (m_isRunning && !component.hasAwakened)
        {
            component.Awake();
            component.hasAwakened = true;
        }

        // Delay Start()
        if (component.isActive && !component.hasStarted)
        {
            m_pendingStartComponents.Add(component);
        }
        
        return component;
    }

    /// <summary>
    /// Removes a component of type T from the entity.
    /// </summary>
    public void Remove<T>(Guid entityId) where T : GameComponent
    {
        if (m_isUpdating)
        {
            m_pendingAddRemoveAction.Add(() => Remove<T>(entityId));
            return;
        }
        
        if (!m_componentsByEntity.TryGetValue(entityId, out var entityComponents))
        {
            return;
        }
        
        var type = typeof(T);

        if (!entityComponents.TryGetValue(type, out var component))
        {
            return;
        }
        
        // Remove the component from the dictionaries
        component.OnDetach();
        if (component.isActive) component.OnDeactivate();
        entityComponents.Remove(type);
        m_componentsByTag[component.orderTag].Remove(component);
        RemoveFromTypeMap(component);

        if (entityComponents.Count == 0)
        {
            m_componentsByEntity.Remove(entityId);
        }
    }
    
    /// <summary>
    /// Removes the specified component instance from the entity.
    /// </summary>
    public void Remove(Guid entityId, GameComponent component)
    {
        if (m_isUpdating)
        {
            m_pendingAddRemoveAction.Add(() => Remove(entityId, component));
            return;
        }
    
        if (!m_componentsByEntity.TryGetValue(entityId, out var entityComponents))
        {
            return;
        }

        var type = component.GetType();

        if (!entityComponents.TryGetValue(type, out var existingComponent))
        {
            return;
        }
    
        if (!ReferenceEquals(existingComponent, component))
        {
            return;
        }
    
        // Remove the component from the dictionaries
        component.OnDetach();
        if (component.isActive) component.OnDeactivate();
        entityComponents.Remove(type);
        m_componentsByTag[component.orderTag].Remove(component);
        RemoveFromTypeMap(component);

        if (entityComponents.Count == 0)
        {
            m_componentsByEntity.Remove(entityId);
        }
    }

    /// <summary>
    /// Gets the component of type T for the entity. Returns null if not found.
    /// </summary>
    public T? Get<T>(Guid entityId) where T : GameComponent
    {
        if (!m_componentsByEntity.TryGetValue(entityId, out var entityComponents))
        {
            return null;
        }
        
        if (entityComponents.TryGetValue(typeof(T), out var component))
        {
            return (T)component;
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets all components of a specific entity.
    /// </summary>
    public IEnumerable<GameComponent> GetAll(Guid entityId)
    {
        if (!m_componentsByEntity.TryGetValue(entityId, out var entityComponents)) {return [];}
        return entityComponents.Values.ToArray();
    }
    
    /// <summary>
    /// Gets all components of a specific type.
    /// </summary>
    public IEnumerable<T> GetAll<T>() where T : GameComponent
    {
        if (!m_componentsByType.TryGetValue(typeof(T), out var comps)) yield break;
        foreach (var c in comps)
        {
            yield return (T)c;
        }
    }

    /// <summary>
    /// Gets all components assignable to a specific type.
    /// </summary>
    public IEnumerable<T> GetAllAssignableTo<T>() where T : GameComponent
    {
        var type = typeof(T);
        foreach (var kvp in m_componentsByType)
        {
            if (type.IsAssignableFrom(kvp.Key))
            {
                foreach (var c in kvp.Value)
                {
                    yield return (T)c;
                }
            }
        }
    }
    
    /// <summary>
    /// Checks if entity has component of type T.
    /// </summary>
    public bool Has<T>(Guid entityId) where T : GameComponent
    {
        return m_componentsByEntity.TryGetValue(entityId, out var entityComponents)
            && entityComponents.ContainsKey(typeof(T));
    }

    /// <summary>
    /// Updates all components ordered by ComponentTag enum.
    /// </summary>
    public void UpdateAll()
    {
        // Execute Starts and Updates
        m_isUpdating = true;
        ProcessPendingStarts();
        UpdateComponents();
        m_isUpdating = false;
        
        // Execute pending adds and removes
        ApplyPendingAddRemoves();
        
        // Execute pending sorts
        ApplyPendingSorts();
    }

    /// <summary>
    /// Wakes up all components ordered by ComponentTag enum. This should be called after all the loading processed
    /// are done.
    /// </summary>
    public void WakeAll()
    {
        foreach (var tag in ORDERED_TAGS)
        {
            foreach (var component in m_componentsByTag[tag])
            {
                if (component.hasAwakened) continue;
                component.Awake();
                component.hasAwakened = true;
            }
        }
    }
    
    public void MarkSortDirty(GameComponent comp)
    {
        if (m_componentsByTag.TryGetValue(comp.orderTag, out var tagList))
        {
            m_pendingToSortLists.Add(tagList);
        }

        var type = comp.GetType();
        if (m_componentsByType.TryGetValue(type, out var typeList))
        {
            m_pendingToSortLists.Add(typeList);
        }
    }
    
    private void ProcessPendingStarts()
    {
        foreach (var component in m_pendingStartComponents)
        {
            if (component.isActive && !component.hasStarted)
            {
                component.Start();
                component.hasStarted = true;
            }
        }
        m_pendingStartComponents.Clear();
    }
    
    private void UpdateComponents()
    {
        foreach (var tag in ORDERED_TAGS)
        {
            foreach (var component in m_componentsByTag[tag])
            {
                if (!component.isActive) {continue;}

                if (component.hasStarted) { component.Update(); }
                else { m_pendingStartComponents.Add(component); }
            }
        }
    }
    
    private void ApplyPendingAddRemoves()
    {
        foreach (var action in m_pendingAddRemoveAction) { action(); }
        m_pendingAddRemoveAction.Clear();
    }

    private void ApplyPendingSorts()
    {
        foreach (var objectList in m_pendingToSortLists) { objectList.Sort(); }
        m_pendingToSortLists.Clear();
    }
    
    private void AddToTypeMap(GameComponent component)
    {
        var type = component.GetType();
        if (!m_componentsByType.TryGetValue(type, out var list))
        {
            list = new List<GameComponent>();
            m_componentsByType[type] = list;
        }
        InsertSorted(list, component);
    }

    private void RemoveFromTypeMap(GameComponent component)
    {
        var type = component.GetType();
        if (m_componentsByType.TryGetValue(type, out var list))
        {
            list.Remove(component);
            if (list.Count == 0) { m_componentsByType.Remove(type); }
        }
    }
    
    private static void InsertSorted(List<GameComponent> list, GameComponent component)
    {
        int index = list.BinarySearch(component);
        if (index < 0) index = ~index;
        list.Insert(index, component);
    }

}
