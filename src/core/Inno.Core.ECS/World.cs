using System;
using System.Collections.Generic;
using Inno.Core.Reflection;
using Inno.Core.Storage;

namespace Inno.Core.ECS;

public sealed class World
{
    private readonly ObjectPool<Entity> m_entities = new();
    private readonly PoolKey<Guid> m_entityIdKey;

    private readonly ObjectPool<Component> m_components = new();
    private readonly PoolKey<Guid> m_componentEntityKey;
    private readonly PoolKey<int> m_componentTypeIdKey;
    
    private readonly record struct ComponentAddOp(Guid entityId, Component component, int componentTypeId);
    private readonly List<ComponentAddOp> m_pendingAddComponents = [];
    
    private readonly record struct ComponentRemoveOp(Guid entityId, int componentTypeId);
    private readonly List<ComponentRemoveOp> m_pendingRemoveComponents = [];

    private readonly HashSet<Guid> m_pendingKilledEntities = [];
    private readonly List<ISystem> m_systems = [];
    
    
    public World()
    {
        m_entityIdKey = m_entities.DefineKey<Guid>("entity.id", PoolKeyFlags.Unique);
        m_componentEntityKey = m_components.DefineKey<Guid>("component.entity");
        m_componentTypeIdKey = m_components.DefineKey<int>("component.typeId");
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

        int componentTypeId = TypeIdentityRegistry.GetOrAddRuntimeTypeId(typeof(TComponent));
        RemovePendingForEntityType(entity.id, componentTypeId);
        m_pendingAddComponents.Add(new ComponentAddOp(entity.id, component, componentTypeId));
    }

    public bool RemoveComponent<TComponent>(Entity entity)
        where TComponent : Component
    {
        ArgumentNullException.ThrowIfNull(entity);
        EnsureEntityExists(entity.id);

        int componentTypeId = TypeIdentityRegistry.GetOrAddRuntimeTypeId(typeof(TComponent));
        bool removedPendingAdd = RemovePendingAdd(entity.id, componentTypeId);
        m_pendingRemoveComponents.Add(new ComponentRemoveOp(entity.id, componentTypeId));
        return removedPendingAdd || FindComponent(entity.id, componentTypeId) is not null;
    }


    public void FlushPending()
    {
        ApplyPendingComponentRemoves();
        ApplyPendingComponentAdds();
        ApplyPendingEntityKills();
    }

    internal IEnumerable<(Entity entity, TComponent component)> QueryTyped<TComponent>()
        where TComponent : Component
    {
        int componentTypeId = TypeIdentityRegistry.GetOrAddRuntimeTypeId(typeof(TComponent));
        IReadOnlyList<Component> typed = m_components.Find(m_componentTypeIdKey, componentTypeId);
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

            Component? existing = FindComponent(op.entityId, op.componentTypeId);
            if (existing is not null)
            {
                RemoveComponentInstance(existing);
            }

            op.component.entityId = op.entityId;
            m_components.Add(op.component)
                .Set(m_componentEntityKey, op.entityId)
                .Set(m_componentTypeIdKey, op.componentTypeId);
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
        return m_components.Query()
            .Find(m_componentEntityKey, entityId)
            .Find(m_componentTypeIdKey, componentTypeId)
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

}
