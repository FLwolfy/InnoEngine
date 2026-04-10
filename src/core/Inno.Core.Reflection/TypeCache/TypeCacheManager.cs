using System;
using System.Collections.Generic;
using System.IO;
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
    private static string? s_currentAssemblyNameFilter;

    private static event Action? OnRefreshed;

    internal static TypeIdentityRegistry identityRegistry { get; } = new();
    internal static TypeQueryRegistry queryRegistry { get; } = new();

    /// <summary>
    /// Initializes TypeCache, runs initialize hooks, and subscribes refresh hooks.
    /// </summary>
    public static void Initialize()
        => Initialize(assemblyName: null);

    /// <summary>
    /// Initializes TypeCache for a specific assembly name, or globally when null/empty.
    /// </summary>
    public static void Initialize(string? assemblyName)
    {
        string? normalizedAssemblyName = NormalizeAssemblyName(assemblyName);
        s_currentAssemblyNameFilter = normalizedAssemblyName;

        Rebuild(normalizedAssemblyName);
        InvokeInitializeHooks(normalizedAssemblyName);
        SubscribeRefreshHooks(normalizedAssemblyName);

        AppDomain.CurrentDomain.AssemblyLoad += (_, _) => s_isDirty = true;
    }

    /// <summary>
    /// Rebuilds TypeCache and identity registry from loaded Inno assemblies.
    /// </summary>
    public static void Rebuild()
        => Rebuild(assemblyName: null);

    /// <summary>
    /// Rebuilds TypeCache and identity registry from a specific assembly name, or globally when null/empty.
    /// </summary>
    public static void Rebuild(string? assemblyName)
    {
        string? normalizedAssemblyName = NormalizeAssemblyName(assemblyName);
        Assembly[] assemblies = ResolveAssemblies(normalizedAssemblyName);
        RebuildFromAssemblies(assemblies);
    }

    /// <summary>
    /// Returns true when the assembly is currently loaded.
    /// </summary>
    public static bool IsAssemblyLoaded(string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            return false;

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic)
            .Any(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Probes assembly names from a directory without loading them into the app domain.
    /// </summary>
    public static IReadOnlyList<string> DiscoverAssemblyNames(string directoryPath, string searchPattern = "*.dll")
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("Directory path is required.", nameof(directoryPath));

        if (!Directory.Exists(directoryPath))
            return [];

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly))
        {
            try
            {
                AssemblyName name = AssemblyName.GetAssemblyName(path);
                if (!string.IsNullOrWhiteSpace(name.Name))
                    names.Add(name.Name);
            }
            catch
            {
                // Ignore non-.NET files or unreadable assemblies.
            }
        }

        return [.. names.OrderBy(static n => n, StringComparer.Ordinal)];
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
            Rebuild(s_currentAssemblyNameFilter);
        }
    }

    private static void RebuildFromAssemblies(Assembly[] assemblies)
    {
        Type[] discoveredTypes = assemblies
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

    private static Assembly[] ResolveAssemblies(string? assemblyName)
    {
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies().Where(static a => !a.IsDynamic).ToArray();
        if (string.IsNullOrWhiteSpace(assemblyName))
            return loaded;

        return loaded
            .Where(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.Ordinal))
            .ToArray();
    }

    private static void InvokeInitializeHooks(string? assemblyNameFilter)
    {
        foreach (MethodInfo method in EnumerateHookMethods(typeof(TypeCacheInitializeAttribute), assemblyNameFilter))
        {
            ValidateHookSignature(method);
            method.Invoke(null, null);
        }
    }

    private static void SubscribeRefreshHooks(string? assemblyNameFilter)
    {
        foreach (MethodInfo method in EnumerateHookMethods(typeof(TypeCacheRebuildAttribute), assemblyNameFilter))
        {
            ValidateHookSignature(method);
            OnRefreshed += () => method.Invoke(null, null);
        }
    }

    private static IEnumerable<MethodInfo> EnumerateHookMethods(Type attributeType, string? assemblyNameFilter)
    {
        Assembly[] assemblies = ResolveAssemblies(assemblyNameFilter);
        IEnumerable<MethodInfo> methods = assemblies
            .SelectMany(static a =>
            {
                try { return a.GetTypes(); }
                catch { return Type.EmptyTypes; }
            })
            .Where(static t => t.Namespace?.StartsWith(C_INNO_NAMESPACE, StringComparison.Ordinal) ?? false)
            .SelectMany(static t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.IsDefined(attributeType, inherit: false));

        foreach (MethodInfo method in methods)
        {
            if (!IsHookAllowedByAttribute(method, assemblyNameFilter, attributeType))
                continue;

            yield return method;
        }
    }

    private static bool IsHookAllowedByAttribute(MethodInfo method, string? assemblyNameFilter, Type attributeType)
    {
        string declaringAssembly = method.DeclaringType?.Assembly.GetName().Name ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(assemblyNameFilter) &&
            !string.Equals(declaringAssembly, assemblyNameFilter, StringComparison.Ordinal))
            return false;

        string? attributeAssembly = null;
        if (attributeType == typeof(TypeCacheInitializeAttribute))
        {
            var attr = method.GetCustomAttribute<TypeCacheInitializeAttribute>(inherit: false);
            attributeAssembly = NormalizeAssemblyName(attr?.assemblyName);
        }
        else if (attributeType == typeof(TypeCacheRebuildAttribute))
        {
            var attr = method.GetCustomAttribute<TypeCacheRebuildAttribute>(inherit: false);
            attributeAssembly = NormalizeAssemblyName(attr?.assemblyName);
        }

        if (string.IsNullOrWhiteSpace(attributeAssembly))
            return true;

        return string.Equals(declaringAssembly, attributeAssembly, StringComparison.Ordinal);
    }

    private static void ValidateHookSignature(MethodInfo method)
    {
        if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
        {
            throw new InvalidOperationException(
                $"TypeCache hook method must be 'static void Method()': {method.DeclaringType?.FullName}.{method.Name}");
        }
    }

    private static string? NormalizeAssemblyName(string? assemblyName)
        => string.IsNullOrWhiteSpace(assemblyName) ? null : assemblyName;

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
