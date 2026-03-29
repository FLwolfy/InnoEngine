using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Inno.Core.Reflection;

/// <summary>
/// Maps runtime <see cref="Type"/> to stable Guid ids and runtime integer ids.
/// </summary>
public static class TypeIdentityRegistry
{
    private static readonly Lock s_sync = new();
    private static Dictionary<Type, Guid> s_stableByType = [];
    private static Dictionary<Guid, Type> s_typeByStable = [];
    private static Dictionary<Type, int> s_runtimeByType = [];
    private static Dictionary<int, Type> s_typeByRuntime = [];
    private static int s_nextRuntimeTypeId = 1;
    private static int s_version;

    /// <summary>
    /// Gets the current registry revision, incremented after every successful rebuild.
    /// </summary>
    public static int version
    {
        get
        {
            lock (s_sync)
            {
                return s_version;
            }
        }
    }

    /// <summary>
    /// Gets the number of registered stable type ids.
    /// </summary>
    public static int stableCount
    {
        get
        {
            lock (s_sync)
            {
                return s_typeByStable.Count;
            }
        }
    }

    /// <summary>
    /// Stable ids are registered only for types decorated with <see cref="StableTypeIdAttribute"/>.
    /// Runtime ids are assigned for all provided types.
    /// </summary>
    /// <param name="types">Types to index.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a decorated type provides an invalid Guid or when multiple types share the same stable id.
    /// </exception>
    public static void Rebuild(IEnumerable<Type> types)
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
        lock (s_sync)
        {
            previousRuntimeByType = s_runtimeByType;
            nextRuntimeTypeId = s_nextRuntimeTypeId;
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

        lock (s_sync)
        {
            s_stableByType = stableByType;
            s_typeByStable = typeByStable;
            s_runtimeByType = runtimeByType;
            s_typeByRuntime = typeByRuntime;
            s_nextRuntimeTypeId = nextRuntimeTypeId;
            s_version++;
        }
    }

    /// <summary>
    /// Rebuilds type identities from loaded, non-dynamic assemblies filtered by namespace prefix.
    /// </summary>
    /// <param name="namespacePrefix">Namespace prefix to include (for example, <c>Inno.Core</c>).</param>
    public static void RebuildFromLoadedAssemblies(string namespacePrefix = "Inno")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespacePrefix);

        Type[] allTypes = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(asm => !asm.IsDynamic)
            .SelectMany(asm =>
            {
                try
                {
                    return asm.GetTypes();
                }
                catch
                {
                    return Type.EmptyTypes;
                }
            })
            .Where(t => t.Namespace?.StartsWith(namespacePrefix, StringComparison.Ordinal) ?? false)
            .ToArray();

        Rebuild(allTypes);
    }

    /// <summary>
    /// Attempts to get the stable id of a type.
    /// </summary>
    /// <param name="type">Target type.</param>
    /// <param name="stableTypeId">Resolved stable id when found.</param>
    /// <returns><c>true</c> when the type is decorated with <see cref="StableTypeIdAttribute"/> and indexed.</returns>
    public static bool TryGetStableTypeId(Type type, out Guid stableTypeId)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (s_sync)
        {
            return s_stableByType.TryGetValue(type, out stableTypeId);
        }
    }

    /// <summary>
    /// Gets the stable id of a type.
    /// </summary>
    /// <param name="type">Target type.</param>
    /// <returns>Stable Guid id.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the type has no stable id mapping.</exception>
    public static Guid GetStableTypeId(Type type)
    {
        if (TryGetStableTypeId(type, out Guid stableTypeId))
        {
            return stableTypeId;
        }

        throw new KeyNotFoundException($"Type '{type.FullName}' has no registered stable id.");
    }

    /// <summary>
    /// Resolves a stable id to its runtime type.
    /// </summary>
    /// <param name="stableTypeId">Stable Guid id.</param>
    /// <param name="type">Resolved runtime type when found.</param>
    /// <returns><c>true</c> when the stable id is registered.</returns>
    public static bool TryResolveType(Guid stableTypeId, out Type? type)
    {
        lock (s_sync)
        {
            if (s_typeByStable.TryGetValue(stableTypeId, out Type? resolved))
            {
                type = resolved;
                return true;
            }
        }

        type = null;
        return false;
    }

    /// <summary>
    /// Attempts to get the runtime id of a type.
    /// </summary>
    /// <param name="type">Target type.</param>
    /// <param name="runtimeTypeId">Resolved runtime id when found.</param>
    /// <returns><c>true</c> when the type is indexed.</returns>
    public static bool TryGetRuntimeTypeId(Type type, out int runtimeTypeId)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (s_sync)
        {
            return s_runtimeByType.TryGetValue(type, out runtimeTypeId);
        }
    }

    /// <summary>
    /// Gets an existing runtime id or allocates a new one for the given type.
    /// </summary>
    /// <param name="type">Target type.</param>
    /// <returns>Runtime integer id.</returns>
    public static int GetOrAddRuntimeTypeId(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (s_sync)
        {
            if (s_runtimeByType.TryGetValue(type, out int existing))
            {
                return existing;
            }

            int runtimeId = s_nextRuntimeTypeId++;
            s_runtimeByType[type] = runtimeId;
            s_typeByRuntime[runtimeId] = type;
            return runtimeId;
        }
    }

    /// <summary>
    /// Gets the runtime id of a type.
    /// </summary>
    /// <param name="type">Target type.</param>
    /// <returns>Runtime integer id.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the type is not indexed.</exception>
    public static int GetRuntimeTypeId(Type type)
    {
        if (TryGetRuntimeTypeId(type, out int runtimeTypeId))
        {
            return runtimeTypeId;
        }

        throw new KeyNotFoundException($"Type '{type.FullName}' has no registered runtime id.");
    }

    /// <summary>
    /// Resolves a runtime id to a type.
    /// </summary>
    /// <param name="runtimeTypeId">Runtime id.</param>
    /// <param name="type">Resolved type when found.</param>
    /// <returns><c>true</c> when the runtime id is registered.</returns>
    public static bool TryResolveRuntimeType(int runtimeTypeId, out Type? type)
    {
        lock (s_sync)
        {
            if (s_typeByRuntime.TryGetValue(runtimeTypeId, out Type? resolved))
            {
                type = resolved;
                return true;
            }
        }

        type = null;
        return false;
    }

    /// <summary>
    /// Creates a stable-id snapshot keyed by lock-file keys (<c>AssemblyName:FullTypeName</c>).
    /// </summary>
    /// <returns>Stable type map snapshot suitable for lock validation.</returns>
    public static IReadOnlyDictionary<string, Guid> GetStableTypeMapSnapshot()
    {
        lock (s_sync)
        {
            var snapshot = new SortedDictionary<string, Guid>(StringComparer.Ordinal);
            foreach ((Type type, Guid stableId) in s_stableByType)
            {
                string typeKey = GetTypeLockKey(type);
                snapshot[typeKey] = stableId;
            }

            return snapshot;
        }
    }

    internal static string GetTypeLockKey(Type type)
    {
        string assemblyName = type.Assembly.GetName().Name ?? "UnknownAssembly";
        string typeName = type.FullName ?? type.Name;
        return $"{assemblyName}:{typeName}";
    }

    [TypeCacheInitialize]
    private static void InitializeFromTypeCache()
        => RebuildFromLoadedAssemblies();

    [TypeCacheRefresh]
    private static void RefreshFromTypeCache()
        => RebuildFromLoadedAssemblies();
}
