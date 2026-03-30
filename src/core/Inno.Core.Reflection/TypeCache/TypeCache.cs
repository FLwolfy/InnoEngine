using System;
using System.Collections.Generic;

namespace Inno.Core.Reflection;

public static class TypeCache
{
    public static IReadOnlyList<Type> GetSubTypesOf<T>()
    {
        TypeCacheManager.EnsureFresh();
        return TypeCacheManager.queryRegistry.GetSubTypesOf<T>(TypeCacheManager.identityRegistry);
    }

    public static IReadOnlyList<Type> GetTypesImplementing<TInterface>()
    {
        TypeCacheManager.EnsureFresh();
        return TypeCacheManager.queryRegistry.GetTypesImplementing<TInterface>(TypeCacheManager.identityRegistry);
    }

    public static IReadOnlyList<Type> GetTypesWithAttribute<TAttr>() where TAttr : Attribute
    {
        TypeCacheManager.EnsureFresh();
        return TypeCacheManager.queryRegistry.GetTypesWithAttribute<TAttr>(TypeCacheManager.identityRegistry);
    }

    public static bool TryGetStableTypeId(Type type, out Guid stableTypeId)
    {
        TypeCacheManager.EnsureFresh();
        if (!TypeCacheManager.identityRegistry.TryGetStableTypeId(type, out stableTypeId))
        {
            return false;
        }

        return TypeCacheManager.IsLoadedStableTypeId(stableTypeId);
    }

    public static bool TryGetRuntimeTypeId(Type type, out int runtimeTypeId)
    {
        TypeCacheManager.EnsureFresh();
        if (!TypeCacheManager.identityRegistry.TryGetRuntimeTypeId(type, out runtimeTypeId))
        {
            return false;
        }

        return TypeCacheManager.IsLoadedRuntimeTypeId(runtimeTypeId);
    }

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
