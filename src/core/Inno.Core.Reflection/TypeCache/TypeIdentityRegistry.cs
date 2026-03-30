using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Inno.Core.Reflection;

/// <summary>
/// Instance-based registry that maps runtime <see cref="Type"/> values to stable and runtime ids.
/// </summary>
internal sealed class TypeIdentityRegistry
{
    private readonly Lock m_sync = new();
    private Dictionary<Type, Guid> m_stableByType = [];
    private Dictionary<Guid, Type> m_typeByStable = [];
    private Dictionary<Type, int> m_runtimeByType = [];
    private Dictionary<int, Type> m_typeByRuntime = [];
    private int m_nextRuntimeTypeId = 1;
    private int m_version;

    public int version
    {
        get
        {
            lock (m_sync)
            {
                return m_version;
            }
        }
    }

    public int stableCount
    {
        get
        {
            lock (m_sync)
            {
                return m_typeByStable.Count;
            }
        }
    }

    public void Rebuild(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);

        Type[] sourceTypes = types
            .Where(static t => t is not null)
            .Distinct()
            .ToArray();

        var stableByType = new Dictionary<Type, Guid>();
        var typeByStable = new Dictionary<Guid, Type>();

        foreach (Type type in sourceTypes)
        {
            StableTypeIdAttribute? attr = type.GetCustomAttribute<StableTypeIdAttribute>(inherit: false);
            if (attr is null)
            {
                continue;
            }

            if (!Guid.TryParse(attr.id, out Guid stableId))
            {
                throw new InvalidOperationException(
                    $"Type '{type.FullName}' has invalid StableTypeId '{attr.id}'.");
            }

            if (!typeByStable.TryAdd(stableId, type))
            {
                Type existing = typeByStable[stableId];
                throw new InvalidOperationException(
                    $"StableTypeId '{stableId}' conflicts between '{existing.FullName}' and '{type.FullName}'.");
            }

            stableByType[type] = stableId;
        }

        var orderedTypes = sourceTypes
            .OrderBy(t => t.Assembly.GetName().Name, StringComparer.Ordinal)
            .ThenBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();

        Dictionary<Type, int> previousRuntimeByType;
        int nextRuntimeTypeId;
        lock (m_sync)
        {
            previousRuntimeByType = m_runtimeByType;
            nextRuntimeTypeId = m_nextRuntimeTypeId;
        }

        var runtimeByType = new Dictionary<Type, int>(orderedTypes.Length);
        var typeByRuntime = new Dictionary<int, Type>(orderedTypes.Length);
        for (int i = 0; i < orderedTypes.Length; i++)
        {
            Type type = orderedTypes[i];
            int runtimeId;
            if (!previousRuntimeByType.TryGetValue(type, out runtimeId))
            {
                runtimeId = nextRuntimeTypeId++;
            }

            runtimeByType[type] = runtimeId;
            typeByRuntime[runtimeId] = type;
        }

        lock (m_sync)
        {
            m_stableByType = stableByType;
            m_typeByStable = typeByStable;
            m_runtimeByType = runtimeByType;
            m_typeByRuntime = typeByRuntime;
            m_nextRuntimeTypeId = nextRuntimeTypeId;
            m_version++;
        }
    }

    public bool TryGetStableTypeId(Type type, out Guid stableTypeId)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (m_sync)
        {
            return m_stableByType.TryGetValue(type, out stableTypeId);
        }
    }

    public bool TryResolveType(Guid stableTypeId, out Type? type)
    {
        lock (m_sync)
        {
            if (m_typeByStable.TryGetValue(stableTypeId, out Type? resolved))
            {
                type = resolved;
                return true;
            }
        }

        type = null;
        return false;
    }

    public bool TryGetRuntimeTypeId(Type type, out int runtimeTypeId)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (m_sync)
        {
            return m_runtimeByType.TryGetValue(type, out runtimeTypeId);
        }
    }

    public int GetOrAddRuntimeTypeId(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (m_sync)
        {
            if (m_runtimeByType.TryGetValue(type, out int existing))
            {
                return existing;
            }

            int runtimeId = m_nextRuntimeTypeId++;
            m_runtimeByType[type] = runtimeId;
            m_typeByRuntime[runtimeId] = type;
            return runtimeId;
        }
    }

    public bool TryResolveRuntimeType(int runtimeTypeId, out Type? type)
    {
        lock (m_sync)
        {
            if (m_typeByRuntime.TryGetValue(runtimeTypeId, out Type? resolved))
            {
                type = resolved;
                return true;
            }
        }

        type = null;
        return false;
    }

    public IReadOnlyDictionary<string, Guid> GetStableTypeMapSnapshot()
    {
        lock (m_sync)
        {
            var snapshot = new SortedDictionary<string, Guid>(StringComparer.Ordinal);
            foreach ((Type type, Guid stableId) in m_stableByType)
            {
                string typeKey = GetTypeLockKey(type);
                snapshot[typeKey] = stableId;
            }

            return snapshot;
        }
    }

    public static string GetTypeLockKey(Type type)
    {
        string assemblyName = type.Assembly.GetName().Name ?? "UnknownAssembly";
        string typeName = type.FullName ?? type.Name;
        return $"{assemblyName}:{typeName}";
    }
}
