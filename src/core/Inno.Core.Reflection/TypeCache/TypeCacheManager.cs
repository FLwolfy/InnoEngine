using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Inno.Core.Reflection;

/// <summary>
/// Manages TypeCache lifecycle, refresh, and hook execution.
/// </summary>
public static class TypeCacheManager
{
    private const string C_INNO_NAMESPACE = "Inno";

    private static readonly Lock CACHE_SYNC = new();

    private static HashSet<int> s_loadedRuntimeTypeIds = [];
    private static HashSet<Guid> s_loadedStableTypeIds = [];
    private static volatile bool s_isDirty;
    private static int s_lastAssemblyCount = -1;

    private static event Action? OnRefreshed;
    
    internal static TypeIdentityRegistry identityRegistry { get; } = new();
    internal static TypeQueryRegistry queryRegistry { get; } = new();

    /// <summary>
    /// Initializes TypeCache, runs initialize hooks, and subscribes refresh hooks.
    /// </summary>
    public static void Initialize()
    {
        Rebuild();
        InvokeInitializeHooks();
        SubscribeRefreshHooks();

        AppDomain.CurrentDomain.AssemblyLoad += (_, _) => s_isDirty = true;
    }

    /// <summary>
    /// Rebuilds TypeCache and identity registry from loaded Inno assemblies.
    /// </summary>
    public static void Rebuild()
    {
        Type[] discoveredTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic)
            .SelectMany(static a =>
            {
                try { return a.GetTypes(); }
                catch { return Type.EmptyTypes; }
            })
            .Where(static t => t.Namespace?.StartsWith(C_INNO_NAMESPACE, StringComparison.Ordinal) ?? false)
            .ToArray();

        identityRegistry.Rebuild(discoveredTypes);
        queryRegistry.Rebuild(discoveredTypes, identityRegistry);

        HashSet<int> loadedRuntimeTypeIds = BuildLoadedRuntimeTypeSet(discoveredTypes);
        HashSet<Guid> loadedStableTypeIds = BuildLoadedStableTypeSet(discoveredTypes);

        lock (CACHE_SYNC)
        {
            s_loadedRuntimeTypeIds = loadedRuntimeTypeIds;
            s_loadedStableTypeIds = loadedStableTypeIds;
            s_lastAssemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;
            s_isDirty = false;
        }

        OnRefreshed?.Invoke();
    }

    internal static bool IsLoadedRuntimeTypeId(int runtimeTypeId)
    {
        lock (CACHE_SYNC)
        {
            return s_loadedRuntimeTypeIds.Contains(runtimeTypeId);
        }
    }

    internal static bool IsLoadedStableTypeId(Guid stableTypeId)
    {
        lock (CACHE_SYNC)
        {
            return s_loadedStableTypeIds.Contains(stableTypeId);
        }
    }

    internal static void EnsureFresh()
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Length != Volatile.Read(ref s_lastAssemblyCount))
        {
            s_isDirty = true;
        }

        if (!s_isDirty)
        {
            return;
        }

        bool shouldRefresh;
        lock (CACHE_SYNC)
        {
            shouldRefresh = s_isDirty;
        }

        if (shouldRefresh)
        {
            Rebuild();
        }
    }

    private static void InvokeInitializeHooks()
    {
        foreach (MethodInfo method in EnumerateHookMethods(typeof(TypeCacheInitializeAttribute)))
        {
            ValidateHookSignature(method);
            method.Invoke(null, null);
        }
    }

    private static void SubscribeRefreshHooks()
    {
        foreach (MethodInfo method in EnumerateHookMethods(typeof(TypeCacheRefreshAttribute)))
        {
            ValidateHookSignature(method);
            OnRefreshed += () => method.Invoke(null, null);
        }
    }

    private static IEnumerable<MethodInfo> EnumerateHookMethods(Type attributeType)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic)
            .SelectMany(static a =>
            {
                try { return a.GetTypes(); }
                catch { return Type.EmptyTypes; }
            })
            .Where(static t => t.Namespace?.StartsWith(C_INNO_NAMESPACE, StringComparison.Ordinal) ?? false)
            .SelectMany(static t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.IsDefined(attributeType, inherit: false));
    }

    private static void ValidateHookSignature(MethodInfo method)
    {
        if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
        {
            throw new InvalidOperationException(
                $"TypeCache hook method must be 'static void Method()': {method.DeclaringType?.FullName}.{method.Name}");
        }
    }

    private static HashSet<int> BuildLoadedRuntimeTypeSet(IEnumerable<Type> types)
    {
        var set = new HashSet<int>();
        foreach (Type type in types)
        {
            if (identityRegistry.TryGetRuntimeTypeId(type, out int runtimeTypeId))
            {
                set.Add(runtimeTypeId);
            }
        }

        return set;
    }

    private static HashSet<Guid> BuildLoadedStableTypeSet(IEnumerable<Type> types)
    {
        var set = new HashSet<Guid>();
        foreach (Type type in types)
        {
            if (identityRegistry.TryGetStableTypeId(type, out Guid stableTypeId))
            {
                set.Add(stableTypeId);
            }
        }

        return set;
    }
}
