using System;
using System.Collections.Generic;
using Inno.Core.Storage;

namespace Inno.Core.ECS;

public sealed class World
{
    private readonly ObjectPool<Entity> m_entities = new();
    private readonly PoolKey<Guid> m_entityIdKey;

    private readonly ObjectPool<Component> m_components = new();
    private readonly PoolKey<Guid> m_componentEntityKey;
    private readonly PoolKey<Type> m_componentTypeKey;

    private readonly List<ISystem> m_systems = [];

    private readonly List<ComponentAddOp> m_pendingAddComponents = [];
    private readonly List<ComponentRemoveOp> m_pendingRemoveComponents = [];
    private readonly HashSet<Guid> m_pendingKilledEntities = [];

    internal PoolKey<Guid> componentEntityKey => m_componentEntityKey;
    internal PoolKey<Type> componentTypeKey => m_componentTypeKey;

    public World()
    {
        m_entityIdKey = m_entities.DefineKey<Guid>("entity.id", PoolKeyFlags.Unique);
        m_componentEntityKey = m_components.DefineKey<Guid>("component.entity");
        m_componentTypeKey = m_components.DefineKey<Type>("component.type");
    }

    public Entity CreateEntity(Guid? parentGuid = null)
    {
        Entity entity = new(Guid.NewGuid(), parentGuid);
        m_entities.Add(entity).Set(m_entityIdKey, entity.id);
        return entity;
    }

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

    public void Update(float deltaTime)
    {
        FlushPending();

        for (int i = 0; i < m_systems.Count; i++)
        {
            m_systems[i].Update(this, deltaTime);
        }

        FlushPending();
    }

    public void AddComponent<TComponent>(Entity entity, TComponent component)
        where TComponent : Component
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(component);

        EnsureEntityExists(entity.id);

        RemovePendingForEntityType(entity.id, typeof(TComponent));
        m_pendingAddComponents.Add(new ComponentAddOp(entity.id, component, typeof(TComponent)));
    }

    public bool RemoveComponent<TComponent>(Entity entity)
        where TComponent : Component
    {
        ArgumentNullException.ThrowIfNull(entity);
        EnsureEntityExists(entity.id);

        bool removedPendingAdd = RemovePendingAdd(entity.id, typeof(TComponent));
        m_pendingRemoveComponents.Add(new ComponentRemoveOp(entity.id, typeof(TComponent)));
        return removedPendingAdd || FindComponent(entity.id, typeof(TComponent)) is not null;
    }

    public QueryBuilder Query()
        => new(this, m_components.Query());

    public void FlushPending()
    {
        ApplyPendingComponentRemoves();
        ApplyPendingComponentAdds();
        ApplyPendingEntityKills();
    }

    internal IEnumerable<(Entity entity, TComponent component)> QueryTyped<TComponent>()
        where TComponent : Component
    {
        IReadOnlyList<Component> typed = m_components.Find(m_componentTypeKey, typeof(TComponent));
        for (int i = 0; i < typed.Count; i++)
        {
            Component component = typed[i];
            if (!TryFindEntity(component.entityId, out Entity? entity) || entity is null)
            {
                continue;
            }

            yield return (entity, (TComponent)component);
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

            Component? existing = FindComponent(op.entityId, op.componentType);
            if (existing is not null)
            {
                RemoveComponentInstance(existing);
            }

            op.component.entityId = op.entityId;
            m_components.Add(op.component)
                .Set(m_componentEntityKey, op.entityId)
                .Set(m_componentTypeKey, op.componentType);
        }

        m_pendingAddComponents.Clear();
    }

    private void ApplyPendingComponentRemoves()
    {
        for (int i = 0; i < m_pendingRemoveComponents.Count; i++)
        {
            ComponentRemoveOp op = m_pendingRemoveComponents[i];
            Component? component = FindComponent(op.entityId, op.componentType);
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

    private Component? FindComponent(Guid entityId, Type componentType)
    {
        return m_components.Query()
            .Find(m_componentEntityKey, entityId)
            .Find(m_componentTypeKey, componentType)
            .First();
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

    private bool RemovePendingAdd(Guid entityId, Type componentType)
    {
        bool removed = false;
        for (int i = m_pendingAddComponents.Count - 1; i >= 0; i--)
        {
            ComponentAddOp op = m_pendingAddComponents[i];
            if (op.entityId != entityId || op.componentType != componentType)
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

    private void RemovePendingForEntityType(Guid entityId, Type componentType)
    {
        for (int i = m_pendingRemoveComponents.Count - 1; i >= 0; i--)
        {
            ComponentRemoveOp op = m_pendingRemoveComponents[i];
            if (op.entityId == entityId && op.componentType == componentType)
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

    private readonly record struct ComponentAddOp(Guid entityId, Component component, Type componentType);

    private readonly record struct ComponentRemoveOp(Guid entityId, Type componentType);
}

public sealed class QueryBuilder(World world, PoolQuery<Component> query)
{
    private readonly World m_world = world;
    private readonly PoolQuery<Component> m_query = query;

    public QueryBuilder ForEntity(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        m_query.Find(m_world.componentEntityKey, entity.id);
        return this;
    }

    public QueryBuilder ForComponent<TComponent>()
        where TComponent : Component
    {
        m_query.Find(m_world.componentTypeKey, typeof(TComponent));
        return this;
    }

    public QueryBuilder Where(Func<Component, bool> predicate)
    {
        m_query.Where(predicate);
        return this;
    }

    public IEnumerable<Component> GetFast()
        => m_query.GetFast();

    public IReadOnlyList<Component> Get()
        => m_query.Get();

    public Component? First()
        => m_query.First();
}
