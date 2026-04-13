using System;
using System.Collections.Generic;

namespace Inno.Core.ECS;

internal sealed class EntityArchetypeIndex
{
    private const int C_EMPTY_ARCHETYPE_ID = 0;

    private readonly Dictionary<int, HashSet<int>> m_entityComponentTypeIds = [];
    private readonly Dictionary<int, int> m_entityArchetypeIds = [];
    private readonly Dictionary<int, HashSet<int>> m_archetypeComponentTypeIdSets = [];
    private readonly Dictionary<string, int> m_archetypeIdBySignature = new(StringComparer.Ordinal);
    private int m_nextArchetypeId = 1;

    public int emptyArchetypeId => C_EMPTY_ARCHETYPE_ID;

    public EntityArchetypeIndex()
    {
        m_archetypeIdBySignature[string.Empty] = C_EMPTY_ARCHETYPE_ID;
        m_archetypeComponentTypeIdSets[C_EMPTY_ARCHETYPE_ID] = [];
    }

    public void RegisterEntity(int entityId)
    {
        m_entityComponentTypeIds[entityId] = [];
        m_entityArchetypeIds[entityId] = C_EMPTY_ARCHETYPE_ID;
    }

    public void UnregisterEntity(int entityId)
    {
        m_entityComponentTypeIds.Remove(entityId);
        m_entityArchetypeIds.Remove(entityId);
    }

    public bool TryOnComponentAdded(int entityId, int componentTypeId, out int archetypeId)
    {
        HashSet<int> componentTypeIds = GetOrCreateEntityComponentTypeSet(entityId);
        if (!componentTypeIds.Add(componentTypeId))
        {
            archetypeId = m_entityArchetypeIds.GetValueOrDefault(entityId, C_EMPTY_ARCHETYPE_ID);
            return false;
        }

        archetypeId = GetOrCreateArchetypeId(componentTypeIds);
        m_entityArchetypeIds[entityId] = archetypeId;
        return true;
    }

    public bool TryOnComponentRemoved(int entityId, int componentTypeId, out int archetypeId)
    {
        if (!m_entityComponentTypeIds.TryGetValue(entityId, out HashSet<int>? componentTypeIds) ||
            !componentTypeIds.Remove(componentTypeId))
        {
            archetypeId = m_entityArchetypeIds.GetValueOrDefault(entityId, C_EMPTY_ARCHETYPE_ID);
            return false;
        }

        archetypeId = GetOrCreateArchetypeId(componentTypeIds);
        m_entityArchetypeIds[entityId] = archetypeId;
        return true;
    }

    public IReadOnlyList<int> GetMatchingArchetypeIds(int[] requiredComponentTypeIds)
    {
        var matches = new List<int>();
        foreach ((int archetypeId, HashSet<int> archetypeSet) in m_archetypeComponentTypeIdSets)
        {
            if (requiredComponentTypeIds.Length > archetypeSet.Count)
            {
                continue;
            }

            bool matched = true;
            for (int i = 0; i < requiredComponentTypeIds.Length; i++)
            {
                if (!archetypeSet.Contains(requiredComponentTypeIds[i]))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                matches.Add(archetypeId);
            }
        }

        return matches;
    }

    private HashSet<int> GetOrCreateEntityComponentTypeSet(int entityId)
    {
        if (m_entityComponentTypeIds.TryGetValue(entityId, out HashSet<int>? componentTypeIds))
        {
            return componentTypeIds;
        }

        var created = new HashSet<int>();
        m_entityComponentTypeIds[entityId] = created;
        return created;
    }

    private int GetOrCreateArchetypeId(HashSet<int> componentTypeIds)
    {
        if (componentTypeIds.Count == 0)
        {
            return C_EMPTY_ARCHETYPE_ID;
        }

        int[] sorted = [..componentTypeIds];
        Array.Sort(sorted);
        string signature = string.Join(",", sorted);
        if (m_archetypeIdBySignature.TryGetValue(signature, out int archetypeId))
        {
            return archetypeId;
        }

        archetypeId = m_nextArchetypeId++;
        m_archetypeIdBySignature[signature] = archetypeId;
        m_archetypeComponentTypeIdSets[archetypeId] = new HashSet<int>(sorted);
        return archetypeId;
    }
}
