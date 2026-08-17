using System;
using System.Collections.Generic;

namespace Inno.Core.Reflection;

/// <summary>
/// Provides queries over the active immutable type catalog.
/// </summary>
public static class TypeCache
{
    /// <summary>
    /// Gets whether an assembly owner has initialized the catalog.
    /// </summary>
    public static bool isInitialized => TypeCacheManager.isInitialized;

    /// <summary>
    /// Gets the current immutable type snapshot.
    /// </summary>
    public static TypeCacheSnapshot current => TypeCacheManager.current;

    /// <summary>
    /// Gets all non-abstract discovered types assignable to <typeparamref name="T"/>.
    /// </summary>
    public static IReadOnlyList<Type> GetSubTypesOf<T>() => current.GetSubTypesOf<T>();

    /// <summary>
    /// Gets all non-abstract discovered types implementing <typeparamref name="TInterface"/>.
    /// </summary>
    public static IReadOnlyList<Type> GetTypesImplementing<TInterface>()
        => current.GetTypesImplementing<TInterface>();

    /// <summary>
    /// Gets all non-abstract discovered types marked with <typeparamref name="TAttribute"/>.
    /// </summary>
    public static IReadOnlyList<Type> GetTypesWithAttribute<TAttribute>() where TAttribute : Attribute
        => current.GetTypesWithAttribute<TAttribute>();

    /// <summary>
    /// Tries to get a stable id for a type in the active catalog.
    /// </summary>
    public static bool TryGetStableTypeId(Type type, out Guid stableTypeId)
        => current.TryGetStableTypeId(type, out stableTypeId);

    /// <summary>
    /// Tries to get a runtime id for a type in the active catalog.
    /// </summary>
    public static bool TryGetRuntimeTypeId(Type type, out int runtimeTypeId)
        => current.TryGetRuntimeTypeId(type, out runtimeTypeId);

    /// <summary>
    /// Tries to resolve an active type by stable id.
    /// </summary>
    public static bool TryResolveType(Guid stableTypeId, out Type? type)
        => current.TryResolveType(stableTypeId, out type);

    /// <summary>
    /// Tries to resolve an active type by runtime id.
    /// </summary>
    public static bool TryResolveType(int runtimeTypeId, out Type? type)
        => current.TryResolveType(runtimeTypeId, out type);
}
