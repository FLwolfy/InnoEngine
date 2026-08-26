using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Inno.Core.Reflection;

/// <summary>
/// Represents an immutable, internally consistent view of discoverable runtime types.
/// </summary>
/// <remarks>
/// A snapshot strongly retains its discovered <see cref="Type"/> instances. Callers must not cache an
/// obsolete snapshot or one of its type lists beyond the operation that needs generation consistency,
/// because doing so delays unloading the corresponding collectible assembly load context.
/// </remarks>
public sealed class TypeCacheSnapshot
{
    internal static TypeCacheSnapshot empty { get; } = CreateEmpty();

    private readonly Type[] m_types;
    private readonly IReadOnlyList<TypeRef> m_typeRefs;
    private readonly Dictionary<Assembly, Type[]> m_typesByAssembly;
    private readonly TypeIdentityRegistry m_identityRegistry;
    private readonly TypeQueryRegistry m_queryRegistry;

    private TypeCacheSnapshot(
        long version,
        Type[] types,
        Dictionary<Assembly, Type[]> typesByAssembly,
        TypeIdentityRegistry identityRegistry,
        TypeQueryRegistry queryRegistry)
    {
        this.version = version;
        m_types = types;
        m_typeRefs = Array.AsReadOnly(types.Select(identityRegistry.GetTypeRef).ToArray());
        m_typesByAssembly = typesByAssembly;
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
    public IReadOnlyList<TypeRef> types => m_typeRefs;

    /// <summary>
    /// Gets all concrete discovered types assignable to <typeparamref name="T"/>.
    /// </summary>
    public IReadOnlyList<TypeRef> GetSubTypesOf<T>()
        => m_queryRegistry.GetSubTypesOf<T>(m_identityRegistry);

    /// <summary>
    /// Gets all concrete discovered types implementing <typeparamref name="TInterface"/>.
    /// </summary>
    public IReadOnlyList<TypeRef> GetTypesImplementing<TInterface>()
        => m_queryRegistry.GetTypesImplementing<TInterface>(m_identityRegistry);

    /// <summary>
    /// Gets all concrete discovered types marked with <typeparamref name="TAttribute"/>.
    /// </summary>
    public IReadOnlyList<TypeRef> GetTypesWithAttribute<TAttribute>() where TAttribute : Attribute
        => m_queryRegistry.GetTypesWithAttribute<TAttribute>(m_identityRegistry);

    /// <summary>
    /// Gets the reference for a CLR type in this snapshot.
    /// </summary>
    /// <param name="type">The CLR type to identify.</param>
    /// <returns>The logical and generation-local identity of <paramref name="type"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the type does not belong to this snapshot.</exception>
    public TypeRef GetTypeRef(Type type) => m_identityRegistry.GetTypeRef(type);

    /// <summary>
    /// Tries to get the reference for a CLR type in this snapshot.
    /// </summary>
    /// <param name="type">The CLR type to identify.</param>
    /// <param name="typeRef">Receives its logical and generation-local identity.</param>
    /// <returns><see langword="true"/> when the type belongs to this snapshot.</returns>
    public bool TryGetTypeRef(Type type, out TypeRef typeRef)
        => m_identityRegistry.TryGetTypeRef(type, out typeRef);

    internal IReadOnlyList<Type> runtimeTypes => m_types;

    internal bool TryResolve(TypeRef typeRef, out Type? type)
        => m_identityRegistry.TryResolveType(typeRef, out type);

    internal static TypeCacheSnapshot Build(
        IEnumerable<Assembly> assemblies,
        TypeCacheSnapshot? previous,
        long version)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var discoveredTypes = new List<Type>();
        var typesByAssembly = new Dictionary<Assembly, Type[]>(ReferenceEqualityComparer.Instance);
        var loaderExceptions = new List<Exception>();
        foreach (Assembly assembly in assemblies.Where(static value => !value.IsDynamic))
        {
            if (typesByAssembly.ContainsKey(assembly))
                continue;
            if (previous is not null && previous.m_typesByAssembly.TryGetValue(assembly, out Type[]? cachedTypes))
            {
                typesByAssembly.Add(assembly, cachedTypes);
                discoveredTypes.AddRange(cachedTypes);
                continue;
            }

            try
            {
                Type[] assemblyTypes = assembly.GetTypes();
                typesByAssembly.Add(assembly, assemblyTypes);
                discoveredTypes.AddRange(assemblyTypes);
            }
            catch (ReflectionTypeLoadException exception)
            {
                Type[] assemblyTypes = exception.Types.OfType<Type>().ToArray();
                typesByAssembly.Add(assembly, assemblyTypes);
                discoveredTypes.AddRange(assemblyTypes);
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
        return new TypeCacheSnapshot(version, types, typesByAssembly, identities, queries);
    }

    private static TypeCacheSnapshot CreateEmpty()
    {
        var identities = new TypeIdentityRegistry();
        identities.Rebuild([], previous: null);
        var queries = new TypeQueryRegistry();
        queries.Rebuild([], identities);
        return new TypeCacheSnapshot(0, [], new Dictionary<Assembly, Type[]>(), identities, queries);
    }
}
