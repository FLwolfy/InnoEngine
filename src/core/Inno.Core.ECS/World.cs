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
    private readonly ObjectPool<Entity> m_entities = new();
    private readonly PoolKey<Guid> m_entityIdKey;

    private readonly ObjectPool<Component> m_components = new();
    private readonly PoolKey<Guid> m_componentEntityKey;
    private readonly PoolKey<int> m_componentTypeIdKey;
    private readonly PoolKey<(Guid entityId, int componentTypeId)> m_componentEntityTypeKey;
    
    private readonly record struct ComponentAddOp(Guid entityId, Component component, int componentTypeId);
    private readonly List<ComponentAddOp> m_pendingAddComponents = [];
    
    private readonly record struct ComponentRemoveOp(Guid entityId, int componentTypeId);
    private readonly List<ComponentRemoveOp> m_pendingRemoveComponents = [];

    private readonly HashSet<Guid> m_pendingKilledEntities = [];
    private readonly List<ISystem> m_systems = [];
    
    
    /// <summary>
    /// Initializes an empty world and all lookup keys.
    /// </summary>
    public World()
    {
        m_entityIdKey = m_entities.DefineKey<Guid>("entity.id", PoolKeyFlags.Unique);
        m_componentEntityKey = m_components.DefineKey<Guid>("component.entity");
        m_componentTypeIdKey = m_components.DefineKey<int>("component.typeId");
        m_componentEntityTypeKey = m_components.DefineKey<(Guid entityId, int componentTypeId)>(
            "component.entityType",
            PoolKeyFlags.Unique);
    }

    /// <summary>
    /// Creates and registers a new entity.
    /// </summary>
    /// <param name="parentGuid">Optional parent entity id.</param>
    /// <returns>The created entity.</returns>
    public Entity CreateEntity(Guid? parentGuid = null)
    {
        Entity entity = new(Guid.NewGuid(), parentGuid);
        m_entities.Add(entity).Set(m_entityIdKey, entity.id);
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
        if (!TryFindEntity(entity.id, out _))
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
        return removedPendingAdd || FindComponent(entity.id, componentTypeId) is not null;
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
    /// </summary>
    /// <typeparam name="TComponent">Component type.</typeparam>
    /// <param name="entityId">Optional entity id filter.</param>
    /// <returns>Snapshot list detached from subsequent world mutations.</returns>
    public IReadOnlyList<TComponent> ViewComponents<TComponent>(Guid? entityId = null)
        where TComponent : Component
    {
        int componentTypeId = GetComponentRuntimeTypeId(typeof(TComponent));
        if (entityId is Guid targetEntityId)
        {
            Component? component = FindComponent(targetEntityId, componentTypeId);
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
    /// </summary>
    /// <typeparam name="TComponent">Component type.</typeparam>
    /// <param name="entityId">Optional entity id filter.</param>
    /// <returns>Lazy sequence suitable for hot-path iteration.</returns>
    public IEnumerable<TComponent> ViewComponentsFast<TComponent>(Guid? entityId = null)
        where TComponent : Component
    {
        int componentTypeId = GetComponentRuntimeTypeId(typeof(TComponent));
        if (entityId is Guid targetEntityId)
        {
            Component? component = FindComponent(targetEntityId, componentTypeId);
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
    /// Returns a stable snapshot of entities that contain all provided component types.
    /// </summary>
    /// <param name="componentTypes">Component type intersection criteria.</param>
    /// <returns>Snapshot list detached from subsequent world mutations.</returns>
    public IReadOnlyList<Entity> ViewEntities(Type[] componentTypes)
    {
        int[] componentTypeIds = ResolveComponentTypeIds(componentTypes);
        IReadOnlyList<Component>[] buckets = new IReadOnlyList<Component>[componentTypeIds.Length];
        int seedIndex = 0;
        for (int i = 0; i < componentTypeIds.Length; i++)
        {
            buckets[i] = m_components.Find(m_componentTypeIdKey, componentTypeIds[i]);
            if (buckets[i].Count < buckets[seedIndex].Count)
            {
                seedIndex = i;
            }
        }

        var result = new List<Entity>(buckets[seedIndex].Count);
        IReadOnlyList<Component> seed = buckets[seedIndex];
        for (int i = 0; i < seed.Count; i++)
        {
            Guid entityId = seed[i].entityId;
            if (!TryFindEntity(entityId, out Entity? entity) || entity is null)
            {
                continue;
            }

            bool matched = true;
            for (int j = 0; j < componentTypeIds.Length; j++)
            {
                if (j == seedIndex)
                {
                    continue;
                }

                if (FindComponent(entityId, componentTypeIds[j]) is null)
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                result.Add(entity);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a lazy fail-fast sequence of entities that contain all provided component types.
    /// </summary>
    /// <param name="componentTypes">Component type intersection criteria.</param>
    /// <returns>Lazy sequence suitable for hot-path iteration.</returns>
    public IEnumerable<Entity> ViewEntitiesFast(Type[] componentTypes)
    {
        return ViewEntitiesFastCore(componentTypes);
    }

    private IEnumerable<Entity> ViewEntitiesFastCore(Type[] componentTypes)
    {
        int[] componentTypeIds = ResolveComponentTypeIds(componentTypes);
        int seedIndex = FindMinBucketIndex(componentTypeIds);
        int seedTypeId = componentTypeIds[seedIndex];
        foreach (Component seedComponent in m_components.FindFast(m_componentTypeIdKey, seedTypeId))
        {
            Guid entityId = seedComponent.entityId;
            if (!TryFindEntity(entityId, out Entity? entity) || entity is null)
            {
                continue;
            }

            bool matched = true;
            for (int i = 0; i < componentTypeIds.Length; i++)
            {
                if (i == seedIndex)
                {
                    continue;
                }

                if (FindComponent(entityId, componentTypeIds[i]) is null)
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
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
                op.component.entityId = Guid.Empty;
                continue;
            }

            if (!TryFindEntity(op.entityId, out _))
            {
                op.component.Reset();
                op.component.entityId = Guid.Empty;
                continue;
            }

            Component? existing = FindComponent(op.entityId, op.componentTypeId);
            if (existing is not null)
            {
                RemoveComponentInstance(existing);
            }

            op.component.entityId = op.entityId;
            m_components.Add(op.component)
                .Set(m_componentEntityKey, op.entityId)
                .Set(m_componentTypeIdKey, op.componentTypeId)
                .Set(m_componentEntityTypeKey, (op.entityId, op.componentTypeId));
        }

        m_pendingAddComponents.Clear();
    }

    private void ApplyPendingComponentRemoves()
    {
        for (int i = 0; i < m_pendingRemoveComponents.Count; i++)
        {
            ComponentRemoveOp op = m_pendingRemoveComponents[i];
            Component? component = FindComponent(op.entityId, op.componentTypeId);
            if (component is null)
            {
                continue;
            }

            RemoveComponentInstance(component);
        }

        m_pendingRemoveComponents.Clear();
    }

    private void ApplyPendingEntityKills()
    {
        if (m_pendingKilledEntities.Count == 0)
        {
            return;
        }

        Guid[] killed = [..m_pendingKilledEntities];
        m_pendingKilledEntities.Clear();

        for (int i = 0; i < killed.Length; i++)
        {
            Guid entityId = killed[i];
            if (!TryFindEntity(entityId, out Entity? entity) || entity is null)
            {
                continue;
            }

            IReadOnlyList<Component> all = m_components.Find(m_componentEntityKey, entityId);
            for (int j = 0; j < all.Count; j++)
            {
                RemoveComponentInstance(all[j]);
            }

            m_entities.Remove(entity);
        }
    }

    private bool RemoveComponentInstance(Component component)
    {
        component.Reset();
        component.entityId = Guid.Empty;
        return m_components.Remove(component);
    }

    private Component? FindComponent(Guid entityId, int componentTypeId)
    {
        return m_components.First(m_componentEntityTypeKey, (entityId, componentTypeId));
    }

    private void EnsureEntityExists(Guid entityId)
    {
        if (!TryFindEntity(entityId, out _))
        {
            throw new InvalidOperationException($"Entity '{entityId}' is not part of this world.");
        }
    }

    private bool TryFindEntity(Guid entityId, out Entity? entity)
    {
        entity = m_entities.First(m_entityIdKey, entityId);
        return entity is not null;
    }

    private bool RemovePendingAdd(Guid entityId, int componentTypeId)
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
            op.component.entityId = Guid.Empty;
            m_pendingAddComponents.RemoveAt(i);
            removed = true;
        }

        return removed;
    }

    private void RemovePendingForEntityType(Guid entityId, int componentTypeId)
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

    private void RemovePendingOpsForEntity(Guid entityId)
    {
        for (int i = m_pendingAddComponents.Count - 1; i >= 0; i--)
        {
            if (m_pendingAddComponents[i].entityId == entityId)
            {
                Component component = m_pendingAddComponents[i].component;
                component.Reset();
                component.entityId = Guid.Empty;
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

    private int FindMinBucketIndex(int[] componentTypeIds)
    {
        int seedIndex = 0;
        int seedCount = m_components.Find(m_componentTypeIdKey, componentTypeIds[0]).Count;
        for (int i = 1; i < componentTypeIds.Length; i++)
        {
            int count = m_components.Find(m_componentTypeIdKey, componentTypeIds[i]).Count;
            if (count < seedCount)
            {
                seedCount = count;
                seedIndex = i;
            }
        }

        return seedIndex;
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
