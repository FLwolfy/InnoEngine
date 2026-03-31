using System;
using System.Collections.Generic;

namespace Inno.Core.Reflection;

/// <summary>
/// Provides query and type-id APIs backed by the global type cache.
/// </summary>
public static class TypeCache
{
    /// <summary>
    /// Gets all non-abstract discovered types assignable to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The base type to query from.</typeparam>
    /// <returns>A read-only list of matching types.</returns>
    public static IReadOnlyList<Type> GetSubTypesOf<T>()
    {
        TypeCacheManager.EnsureFresh();
        return TypeCacheManager.queryRegistry.GetSubTypesOf<T>(TypeCacheManager.identityRegistry);
    }

    /// <summary>
    /// Gets all non-abstract discovered types implementing <typeparamref name="TInterface"/>.
    /// </summary>
    /// <typeparam name="TInterface">The interface type to query.</typeparam>
    /// <returns>A read-only list of matching types.</returns>
    public static IReadOnlyList<Type> GetTypesImplementing<TInterface>()
    {
        TypeCacheManager.EnsureFresh();
        return TypeCacheManager.queryRegistry.GetTypesImplementing<TInterface>(TypeCacheManager.identityRegistry);
    }

    /// <summary>
    /// Gets all non-abstract discovered types marked with <typeparamref name="TAttr"/>.
    /// </summary>
    /// <typeparam name="TAttr">The attribute type to query.</typeparam>
    /// <returns>A read-only list of matching types.</returns>
    public static IReadOnlyList<Type> GetTypesWithAttribute<TAttr>() where TAttr : Attribute
    {
        TypeCacheManager.EnsureFresh();
        return TypeCacheManager.queryRegistry.GetTypesWithAttribute<TAttr>(TypeCacheManager.identityRegistry);
    }

    /// <summary>
    /// Tries to get a loaded stable type id for <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The runtime type to resolve.</param>
    /// <param name="stableTypeId">The resolved stable id when successful.</param>
    /// <returns><see langword="true"/> when the type has a stable id and is currently loaded; otherwise <see langword="false"/>.</returns>
    public static bool TryGetStableTypeId(Type type, out Guid stableTypeId)
    {
        TypeCacheManager.EnsureFresh();
        if (!TypeCacheManager.identityRegistry.TryGetStableTypeId(type, out stableTypeId))
        {
            return false;
        }

        return TypeCacheManager.IsLoadedStableTypeId(stableTypeId);
    }

    /// <summary>
    /// Tries to get a loaded runtime type id for <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The runtime type to resolve.</param>
    /// <param name="runtimeTypeId">The resolved runtime id when successful.</param>
    /// <returns><see langword="true"/> when the type is currently loaded; otherwise <see langword="false"/>.</returns>
    public static bool TryGetRuntimeTypeId(Type type, out int runtimeTypeId)
    {
        TypeCacheManager.EnsureFresh();
        if (!TypeCacheManager.identityRegistry.TryGetRuntimeTypeId(type, out runtimeTypeId))
        {
            return false;
        }

        return TypeCacheManager.IsLoadedRuntimeTypeId(runtimeTypeId);
    }

    /// <summary>
    /// Tries to resolve a loaded type by stable id.
    /// </summary>
    /// <param name="stableTypeId">The stable type id to resolve.</param>
    /// <param name="type">The resolved type when successful.</param>
    /// <returns><see langword="true"/> when the id is loaded and resolved; otherwise <see langword="false"/>.</returns>
    public static bool TryResolveType(Guid stableTypeId, out Type? type)
    {
        TypeCacheManager.EnsureFresh();
        if (!TypeCacheManager.IsLoadedStableTypeId(stableTypeId))
        {
            type = null;
            return false;
        }

        return TypeCacheManager.identityRegistry.TryResolveType(stableTypeId, out type);
    }

    /// <summary>
    /// Tries to resolve a loaded type by runtime id.
    /// </summary>
    /// <param name="runtimeTypeId">The runtime type id to resolve.</param>
    /// <param name="type">The resolved type when successful.</param>
    /// <returns><see langword="true"/> when the id is loaded and resolved; otherwise <see langword="false"/>.</returns>
    public static bool TryResolveType(int runtimeTypeId, out Type? type)
    {
        TypeCacheManager.EnsureFresh();
        if (!TypeCacheManager.IsLoadedRuntimeTypeId(runtimeTypeId))
        {
            type = null;
            return false;
        }

        return TypeCacheManager.identityRegistry.TryResolveRuntimeType(runtimeTypeId, out type);
    }
}
