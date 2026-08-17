using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Inno.Core.Reflection;

/// <summary>
/// Represents an immutable, internally consistent view of discoverable runtime types.
/// </summary>
public sealed class TypeCacheSnapshot
{
    internal static TypeCacheSnapshot empty { get; } = CreateEmpty();

    private readonly Type[] m_types;
    private readonly TypeIdentityRegistry m_identityRegistry;
    private readonly TypeQueryRegistry m_queryRegistry;

    private TypeCacheSnapshot(
        long version,
        Type[] types,
        TypeIdentityRegistry identityRegistry,
        TypeQueryRegistry queryRegistry)
    {
        this.version = version;
        m_types = types;
        m_identityRegistry = identityRegistry;
        m_queryRegistry = queryRegistry;
    }

    /// <summary>
    /// Gets the monotonically increasing catalog version.
    /// </summary>
    public long version { get; }

    /// <summary>
    /// Gets every type included in this snapshot.
    /// </summary>
    public IReadOnlyList<Type> types => m_types;

    /// <summary>
    /// Gets all concrete discovered types assignable to <typeparamref name="T"/>.
    /// </summary>
    public IReadOnlyList<Type> GetSubTypesOf<T>()
        => m_queryRegistry.GetSubTypesOf<T>(m_identityRegistry);

    /// <summary>
    /// Gets all concrete discovered types implementing <typeparamref name="TInterface"/>.
    /// </summary>
    public IReadOnlyList<Type> GetTypesImplementing<TInterface>()
        => m_queryRegistry.GetTypesImplementing<TInterface>(m_identityRegistry);

    /// <summary>
    /// Gets all concrete discovered types marked with <typeparamref name="TAttribute"/>.
    /// </summary>
    public IReadOnlyList<Type> GetTypesWithAttribute<TAttribute>() where TAttribute : Attribute
        => m_queryRegistry.GetTypesWithAttribute<TAttribute>(m_identityRegistry);

    /// <summary>
    /// Tries to resolve the stable identity of a type in this snapshot.
    /// </summary>
    public bool TryGetStableTypeId(Type type, out Guid stableTypeId)
        => m_identityRegistry.TryGetStableTypeId(type, out stableTypeId);

    /// <summary>
    /// Tries to resolve the runtime identity of a type in this snapshot.
    /// </summary>
    public bool TryGetRuntimeTypeId(Type type, out int runtimeTypeId)
        => m_identityRegistry.TryGetRuntimeTypeId(type, out runtimeTypeId);

    /// <summary>
    /// Tries to resolve a current type by stable identity.
    /// </summary>
    public bool TryResolveType(Guid stableTypeId, out Type? type)
        => m_identityRegistry.TryResolveType(stableTypeId, out type);

    /// <summary>
    /// Tries to resolve a current type by runtime identity.
    /// </summary>
    public bool TryResolveType(int runtimeTypeId, out Type? type)
        => m_identityRegistry.TryResolveRuntimeType(runtimeTypeId, out type);

    internal static TypeCacheSnapshot Build(
        IEnumerable<Assembly> assemblies,
        TypeCacheSnapshot? previous,
        long version)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var discoveredTypes = new List<Type>();
        var loaderExceptions = new List<Exception>();
        foreach (Assembly assembly in assemblies.Where(static value => !value.IsDynamic).Distinct())
        {
            try
            {
                discoveredTypes.AddRange(assembly.GetTypes());
            }
            catch (ReflectionTypeLoadException exception)
            {
                discoveredTypes.AddRange(exception.Types.OfType<Type>());
                loaderExceptions.AddRange(exception.LoaderExceptions.OfType<Exception>());
            }
            catch (Exception exception)
            {
                loaderExceptions.Add(exception);
            }
        }

        if (loaderExceptions.Count > 0)
        {
            throw new TypeCacheBuildException(
                $"Type discovery failed with {loaderExceptions.Count} loader error(s).",
                loaderExceptions);
        }

        Type[] types = discoveredTypes
            .Distinct()
            .OrderBy(static type => type.Assembly.GetName().Name, StringComparer.Ordinal)
            .ThenBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var identities = new TypeIdentityRegistry();
        identities.Rebuild(types, previous?.m_identityRegistry);
        var queries = new TypeQueryRegistry();
        queries.Rebuild(types, identities);
        return new TypeCacheSnapshot(version, types, identities, queries);
    }

    private static TypeCacheSnapshot CreateEmpty()
    {
        var identities = new TypeIdentityRegistry();
        identities.Rebuild([], previous: null);
        var queries = new TypeQueryRegistry();
        queries.Rebuild([], identities);
        return new TypeCacheSnapshot(0, [], identities, queries);
    }
}
