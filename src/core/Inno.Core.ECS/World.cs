using System;
using System.Collections.Generic;
using Inno.Core.Reflection;
using Inno.Core.Storage;

namespace Inno.Core.ECS;

/// <summary>
/// ECS runtime container that manages entities, components, and system updates.
/// </summary>
public sealed class World
{
    private readonly WeakReference<World> m_worldRef;

    private readonly ObjectPool<Entity> m_entities = new();
    private readonly PoolKey<int> m_entityIdKey;
    private readonly PoolKey<int> m_entityArchetypeIdKey;

    private readonly ObjectPool<Component> m_components = new();
    private readonly PoolKey<int> m_componentEntityKey;
    private readonly PoolKey<int> m_componentTypeIdKey;
    private readonly PoolKey<(int entityId, int componentTypeId)> m_componentEntityTypeKey;
    
    private readonly record struct ComponentAddOp(int entityId, Component component, int componentTypeId);
    private readonly List<ComponentAddOp> m_pendingAddComponents = [];
    
    private readonly record struct ComponentRemoveOp(int entityId, int componentTypeId);
    private readonly List<ComponentRemoveOp> m_pendingRemoveComponents = [];

    private readonly HashSet<int> m_pendingKilledEntities = [];
    private readonly List<ISystem> m_systems = [];
    private readonly EntityArchetypeIndex m_archetypeIndex = new();
    private int m_nextEntityId = 1;
    
    /// <summary>
    /// Initializes an empty world and all lookup keys.
    /// </summary>
    public World()
    {
        m_worldRef = new WeakReference<World>(this);
        m_entityIdKey = m_entities.DefineKey<int>("entity.id", PoolKeyFlags.Unique);
        m_entityArchetypeIdKey = m_entities.DefineKey<int>("entity.archetypeId");
        m_componentEntityKey = m_components.DefineKey<int>("component.entity");
        m_componentTypeIdKey = m_components.DefineKey<int>("component.typeId");
        m_componentEntityTypeKey = m_components.DefineKey<(int entityId, int componentTypeId)>(
            "component.entityType",
            PoolKeyFlags.Unique);
    }

    /// <summary>
    /// Creates and registers a new entity.
    /// </summary>
    /// <returns>The created entity.</returns>
    public Entity CreateEntity()
    {
        if (m_nextEntityId <= 0)
        {
            throw new InvalidOperationException("Entity id pool exhausted.");
        }

        Entity entity = new(m_nextEntityId++);
        m_entities.Add(entity)
            .Set(m_entityIdKey, entity.id)
            .Set(m_entityArchetypeIdKey, m_archetypeIndex.emptyArchetypeId);
        m_archetypeIndex.RegisterEntity(entity.id);
        return entity;
    }

    /// <summary>
    /// Marks an entity for deferred destruction.
    /// </summary>
    /// <param name="entity">Entity to destroy.</param>
    /// <returns><see langword="true"/> if newly scheduled; otherwise <see langword="false"/>.</returns>
    public bool KillEntity(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (m_entities.First(m_entityIdKey, entity.id) is null)
        {
            return false;
        }

        RemovePendingOpsForEntity(entity.id);
        return m_pendingKilledEntities.Add(entity.id);
    }

    /// <summary>
    /// Registers a system and keeps execution order deterministic by <see cref="ISystem.order"/>.
    /// </summary>
    /// <param name="system">System instance to register.</param>
    public void RegisterSystem(ISystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        m_systems.Add(system);
        m_systems.Sort(static (a, b) =>
        {
            int order = a.order.CompareTo(b.order);
            if (order != 0)
            {
                return order;
            }

            string? aName = a.GetType().FullName;
            string? bName = b.GetType().FullName;
            return string.CompareOrdinal(aName, bName);
        });
    }

    /// <summary>
    /// Unregisters the first system assignable to <typeparamref name="TSystem"/>.
    /// </summary>
    /// <typeparam name="TSystem">System type to remove.</typeparam>
    /// <returns><see langword="true"/> if removed; otherwise <see langword="false"/>.</returns>
    public bool UnregisterSystem<TSystem>()
        where TSystem : class, ISystem
    {
        for (int i = 0; i < m_systems.Count; i++)
        {
            if (m_systems[i] is TSystem)
            {
                m_systems.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Updates all systems for a frame.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public void Update(float deltaTime)
    {
        FlushPending();

        for (int i = 0; i < m_systems.Count; i++)
        {
            m_systems[i].Update(this, deltaTime);
        }

        FlushPending();
    }

    /// <summary>
    /// Schedules creation of a new component instance on an entity.
    /// </summary>
    /// <typeparam name="TComponent">Component type to add.</typeparam>
    /// <param name="entity">Target entity.</param>
    public void AddComponent<TComponent>(Entity entity)
        where TComponent : Component, new()
    {
        ArgumentNullException.ThrowIfNull(entity);

        EnsureEntityExists(entity.id);

        TComponent component = new();
        int componentTypeId = GetComponentRuntimeTypeId(typeof(TComponent));
        RemovePendingForEntityType(entity.id, componentTypeId);
        m_pendingAddComponents.Add(new ComponentAddOp(entity.id, component, componentTypeId));
    }

    /// <summary>
    /// Schedules removal of a component type from an entity.
    /// </summary>
    /// <typeparam name="TComponent">Component type to remove.</typeparam>
    /// <param name="entity">Target entity.</param>
    /// <returns><see langword="true"/> if an existing or pending component was found.</returns>
    public bool RemoveComponent<TComponent>(Entity entity)
        where TComponent : Component
    {
        ArgumentNullException.ThrowIfNull(entity);
        EnsureEntityExists(entity.id);

        int componentTypeId = GetComponentRuntimeTypeId(typeof(TComponent));
        bool removedPendingAdd = RemovePendingAdd(entity.id, componentTypeId);
        m_pendingRemoveComponents.Add(new ComponentRemoveOp(entity.id, componentTypeId));
        return removedPendingAdd || m_components.First(m_componentEntityTypeKey, (entity.id, componentTypeId)) is not null;
    }


    /// <summary>
    /// Applies all deferred add/remove/destroy operations immediately.
    /// </summary>
    public void FlushPending()
    {
        ApplyPendingComponentRemoves();
        ApplyPendingComponentAdds();
        ApplyPendingEntityKills();
    }

    /// <summary>
    /// Returns a stable snapshot of components matching the requested type and optional entity filter.
    /// When <paramref name="entityId"/> is <see langword="null"/>, returns all components of <typeparamref name="TComponent"/>.
    /// </summary>
    /// <typeparam name="TComponent">Component type.</typeparam>
    /// <param name="entityId">Optional entity id filter.</param>
    /// <returns>Snapshot list detached from subsequent world mutations.</returns>
    public IReadOnlyList<TComponent> ViewComponents<TComponent>(int? entityId = null)
        where TComponent : Component
    {
        int componentTypeId = GetComponentRuntimeTypeId(typeof(TComponent));
        if (entityId is int targetEntityId)
        {
            Component? component = m_components.First(m_componentEntityTypeKey, (targetEntityId, componentTypeId));
            if (component is TComponent typed)
            {
                return [typed];
            }

            return [];
        }

        IReadOnlyList<Component> typedComponents = m_components.Find(m_componentTypeIdKey, componentTypeId);
        var result = new List<TComponent>(typedComponents.Count);
        for (int i = 0; i < typedComponents.Count; i++)
        {
            if (typedComponents[i] is TComponent typed)
            {
                result.Add(typed);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a lazy fail-fast sequence of components matching the requested type and optional entity filter.
    /// When <paramref name="entityId"/> is <see langword="null"/>, returns all components of <typeparamref name="TComponent"/>.
    /// </summary>
    /// <typeparam name="TComponent">Component type.</typeparam>
    /// <param name="entityId">Optional entity id filter.</param>
    /// <returns>Lazy sequence suitable for hot-path iteration.</returns>
    public IEnumerable<TComponent> ViewComponentsFast<TComponent>(int? entityId = null)
        where TComponent : Component
    {
        int componentTypeId = GetComponentRuntimeTypeId(typeof(TComponent));
        if (entityId is int targetEntityId)
        {
            Component? component = m_components.First(m_componentEntityTypeKey, (targetEntityId, componentTypeId));
            if (component is TComponent typed)
            {
                yield return typed;
            }

            yield break;
        }

        foreach (Component component in m_components.FindFast(m_componentTypeIdKey, componentTypeId))
        {
            if (component is TComponent typed)
            {
                yield return typed;
            }
        }
    }

    /// <summary>
    /// Creates a reusable handle for querying entities that contain all provided component types.
    /// </summary>
    /// <param name="componentTypes">Component type intersection criteria.</param>
    /// <returns>Reusable handle for <see cref="ViewEntities(EntityViewHandle)"/> and <see cref="ViewEntitiesFast(EntityViewHandle)"/>.</returns>
    public EntityViewHandle CreateEntityViewHandle(Type[] componentTypes)
    {
        int[] componentTypeIds = ResolveComponentTypeIds(componentTypes);
        return new EntityViewHandle(m_worldRef, componentTypeIds);
    }

    /// <summary>
    /// Returns a stable snapshot of entities that match a prebuilt entity view handle.
    /// When <paramref name="handle"/> is <see langword="null"/>, returns all entities.
    /// </summary>
    /// <param name="handle">Prebuilt view handle.</param>
    /// <returns>Snapshot list detached from subsequent world mutations.</returns>
    public IReadOnlyList<Entity> ViewEntities(EntityViewHandle? handle = null)
    {
        if (handle is null)
        {
            return m_entities.All();
        }

        int[] componentTypeIds = handle.Value.GetComponentTypeIdsOrThrow(this);
        IReadOnlyList<int> archetypeIds = m_archetypeIndex.GetMatchingArchetypeIds(componentTypeIds);
        if (archetypeIds.Count == 0)
        {
            return [];
        }

        var result = new List<Entity>();
        for (int i = 0; i < archetypeIds.Count; i++)
        {
            IReadOnlyList<Entity> entities = m_entities.Find(m_entityArchetypeIdKey, archetypeIds[i]);
            for (int j = 0; j < entities.Count; j++)
            {
                result.Add(entities[j]);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a lazy fail-fast sequence of entities that match a prebuilt entity view handle.
    /// When <paramref name="handle"/> is <see langword="null"/>, returns all entities.
    /// </summary>
    /// <param name="handle">Prebuilt view handle.</param>
    /// <returns>Lazy sequence suitable for hot-path iteration.</returns>
    public IEnumerable<Entity> ViewEntitiesFast(EntityViewHandle? handle = null)
    {
        if (handle is null)
        {
            foreach (Entity entity in m_entities.AllFast())
            {
                yield return entity;
            }

            yield break;
        }

        int[] componentTypeIds = handle.Value.GetComponentTypeIdsOrThrow(this);
        IReadOnlyList<int> archetypeIds = m_archetypeIndex.GetMatchingArchetypeIds(componentTypeIds);
        for (int i = 0; i < archetypeIds.Count; i++)
        {
            foreach (Entity entity in m_entities.FindFast(m_entityArchetypeIdKey, archetypeIds[i]))
            {
                yield return entity;
            }
        }
    }

    private void ApplyPendingComponentAdds()
    {
        for (int i = 0; i < m_pendingAddComponents.Count; i++)
        {
            ComponentAddOp op = m_pendingAddComponents[i];
            if (m_pendingKilledEntities.Contains(op.entityId))
            {
                op.component.Reset();
                op.component.entityId = 0;
                continue;
            }

            if (m_entities.First(m_entityIdKey, op.entityId) is null)
            {
                op.component.Reset();
                op.component.entityId = 0;
                continue;
            }

            Component? existing = m_components.First(m_componentEntityTypeKey, (op.entityId, op.componentTypeId));
            if (existing is not null)
            {
                RemoveComponentInstance(existing);
            }

            op.component.entityId = op.entityId;
            m_components.Add(op.component)
                .Set(m_componentEntityKey, op.entityId)
                .Set(m_componentTypeIdKey, op.componentTypeId)
                .Set(m_componentEntityTypeKey, (op.entityId, op.componentTypeId));

            if (m_archetypeIndex.TryOnComponentAdded(op.entityId, op.componentTypeId, out int archetypeId))
            {
                Entity? entity = m_entities.First(m_entityIdKey, op.entityId);
                if (entity is not null)
                {
                    m_entities.Add(entity).Set(m_entityArchetypeIdKey, archetypeId);
                }
            }
        }

        m_pendingAddComponents.Clear();
    }

    private void ApplyPendingComponentRemoves()
    {
        for (int i = 0; i < m_pendingRemoveComponents.Count; i++)
        {
            ComponentRemoveOp op = m_pendingRemoveComponents[i];
            Component? component = m_components.First(m_componentEntityTypeKey, (op.entityId, op.componentTypeId));
            if (component is null)
            {
                continue;
            }

            RemoveComponentInstance(component);
            if (m_archetypeIndex.TryOnComponentRemoved(op.entityId, op.componentTypeId, out int archetypeId))
            {
                Entity? entity = m_entities.First(m_entityIdKey, op.entityId);
                if (entity is not null)
                {
                    m_entities.Add(entity).Set(m_entityArchetypeIdKey, archetypeId);
                }
            }
        }

        m_pendingRemoveComponents.Clear();
    }

    private void ApplyPendingEntityKills()
    {
        if (m_pendingKilledEntities.Count == 0)
        {
            return;
        }

        int[] killed = [..m_pendingKilledEntities];
        m_pendingKilledEntities.Clear();

        for (int i = 0; i < killed.Length; i++)
        {
            int entityId = killed[i];
            Entity? entity = m_entities.First(m_entityIdKey, entityId);
            if (entity is null)
            {
                continue;
            }

            IReadOnlyList<Component> all = m_components.Find(m_componentEntityKey, entityId);
            for (int j = 0; j < all.Count; j++)
            {
                RemoveComponentInstance(all[j]);
            }

            m_archetypeIndex.UnregisterEntity(entityId);
            m_entities.Remove(entity);
        }
    }

    private bool RemoveComponentInstance(Component component)
    {
        component.Reset();
        component.entityId = 0;
        return m_components.Remove(component);
    }

    private void EnsureEntityExists(int entityId)
    {
        if (m_entities.First(m_entityIdKey, entityId) is null)
        {
            throw new InvalidOperationException($"Entity '{entityId}' is not part of this world.");
        }
    }

    private bool RemovePendingAdd(int entityId, int componentTypeId)
    {
        bool removed = false;
        for (int i = m_pendingAddComponents.Count - 1; i >= 0; i--)
        {
            ComponentAddOp op = m_pendingAddComponents[i];
            if (op.entityId != entityId || op.componentTypeId != componentTypeId)
            {
                continue;
            }

            op.component.Reset();
            op.component.entityId = 0;
            m_pendingAddComponents.RemoveAt(i);
            removed = true;
        }

        return removed;
    }

    private void RemovePendingForEntityType(int entityId, int componentTypeId)
    {
        for (int i = m_pendingRemoveComponents.Count - 1; i >= 0; i--)
        {
            ComponentRemoveOp op = m_pendingRemoveComponents[i];
            if (op.entityId == entityId && op.componentTypeId == componentTypeId)
            {
                m_pendingRemoveComponents.RemoveAt(i);
            }
        }
    }

    private void RemovePendingOpsForEntity(int entityId)
    {
        for (int i = m_pendingAddComponents.Count - 1; i >= 0; i--)
        {
            if (m_pendingAddComponents[i].entityId == entityId)
            {
                Component component = m_pendingAddComponents[i].component;
                component.Reset();
                component.entityId = 0;
                m_pendingAddComponents.RemoveAt(i);
            }
        }

        for (int i = m_pendingRemoveComponents.Count - 1; i >= 0; i--)
        {
            if (m_pendingRemoveComponents[i].entityId == entityId)
            {
                m_pendingRemoveComponents.RemoveAt(i);
            }
        }
    }

    private static int GetComponentRuntimeTypeId(Type componentType)
    {
        if (TypeCache.TryGetRuntimeTypeId(componentType, out int runtimeTypeId))
        {
            return runtimeTypeId;
        }

        throw new InvalidOperationException(
            $"Component type '{componentType.FullName}' is not loaded in TypeCache. Call TypeCacheManager.Initialize/Rebuild first.");
    }

    private static int[] ResolveComponentTypeIds(Type[] componentTypes)
    {
        ArgumentNullException.ThrowIfNull(componentTypes);
        if (componentTypes.Length == 0)
        {
            throw new ArgumentException("At least one component type is required.", nameof(componentTypes));
        }

        var seen = new HashSet<int>();
        var typeIds = new List<int>(componentTypes.Length);
        for (int i = 0; i < componentTypes.Length; i++)
        {
            Type componentType = componentTypes[i] ?? throw new ArgumentNullException(nameof(componentTypes));
            if (!typeof(Component).IsAssignableFrom(componentType))
            {
                throw new ArgumentException(
                    $"Type '{componentType.FullName}' is not a component type.",
                    nameof(componentTypes));
            }

            int typeId = GetComponentRuntimeTypeId(componentType);
            if (seen.Add(typeId))
            {
                typeIds.Add(typeId);
            }
        }

        return [..typeIds];
    }

}
