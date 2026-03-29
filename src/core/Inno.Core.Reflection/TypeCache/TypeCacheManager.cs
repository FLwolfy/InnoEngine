using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Inno.Core.Reflection;

/// <summary>
/// Provides cached type discovery for Inno namespaces, including subtype, interface, and attribute lookups.
/// </summary>
public static class TypeCacheManager
{
    private const string C_INNO_NAMESPACE = "Inno";

    private static Dictionary<int, Type[]> s_subclassCache = [];
    private static Dictionary<int, Type[]> s_interfaceCache = [];
    private static Dictionary<int, Type[]> s_attributeCache = [];

    private static readonly Lock CACHE_SYNC = new();
    private static volatile bool s_isDirty = false;
    private static int s_lastAssemblyCount = -1;

    private static event Action? OnRefreshed;

    /// <summary>
    /// Initializes the cache manager, runs initialize hooks, and subscribes refresh hooks.
    /// </summary>
    public static void Initialize()
    {
        Refresh();
        
        InvokeInitializeHooks();
        SubscribeRefreshHooks();
            
        AppDomain.CurrentDomain.AssemblyLoad += (_, _) =>
        {
            s_isDirty = true;
        };

        OnRefreshed?.Invoke();
    }
    
    private static void InvokeInitializeHooks()
    {
        foreach (var method in EnumerateHookMethods(typeof(TypeCacheInitializeAttribute)))
        {
            ValidateHookSignature(method);
            method.Invoke(null, null);
        }
    }

    private static void SubscribeRefreshHooks()
    {
        foreach (var method in EnumerateHookMethods(typeof(TypeCacheRefreshAttribute)))
        {
            ValidateHookSignature(method);
            OnRefreshed += () => method.Invoke(null, null);
        }
    }

    private static IEnumerable<MethodInfo> EnumerateHookMethods(Type attributeType)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic);

        var allTypes = assemblies
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Type.EmptyTypes; }
            })
            .Where(t => t.Namespace?.StartsWith(C_INNO_NAMESPACE) ?? false);

        foreach (var type in allTypes)
        {
            var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var method in methods)
            {
                if (method.IsDefined(attributeType, inherit: false))
                    yield return method;
            }
        }
    }

    private static void ValidateHookSignature(MethodInfo method)
    {
        if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
        {
            throw new InvalidOperationException(
                $"[{method.GetCustomAttributes(false).First(a => a.GetType().Name.Contains("TypeCache"))}] " + $"method must be 'static void Method()': {method.DeclaringType?.FullName}.{method.Name}");
        }
    }
    
    /// <summary>
    /// Rebuilds all internal type indices from currently loaded assemblies.
    /// </summary>
    public static void Refresh()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic);

        var allTypes = assemblies
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Type.EmptyTypes; }
            })
            .Where(t => !t.IsAbstract && !t.IsInterface && (t.Namespace?.StartsWith(C_INNO_NAMESPACE) ?? false))
            .ToArray();

        var subclassSets = new Dictionary<int, HashSet<Type>>();
        var interfaceSets = new Dictionary<int, HashSet<Type>>();
        var attributeSets = new Dictionary<int, HashSet<Type>>();

        foreach (var type in allTypes)
        {
            if (type.IsAbstract) continue;

            // Index by base type
            var baseType = type.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                AddToIndex(subclassSets, baseType, type);
                baseType = baseType.BaseType;
            }

            // Index by interfaces
            foreach (var iface in type.GetInterfaces())
            {
                AddToIndex(interfaceSets, iface, type);
            }

            // Index by attributes
            foreach (var attr in type.GetCustomAttributes(inherit: true))
            {
                var attrType = attr.GetType();
                AddToIndex(attributeSets, attrType, type);
            }
        }

        Dictionary<int, Type[]> subclassCache = FreezeIndex(subclassSets);
        Dictionary<int, Type[]> interfaceCache = FreezeIndex(interfaceSets);
        Dictionary<int, Type[]> attributeCache = FreezeIndex(attributeSets);

        lock (CACHE_SYNC)
        {
            s_subclassCache = subclassCache;
            s_interfaceCache = interfaceCache;
            s_attributeCache = attributeCache;
            s_lastAssemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;
            s_isDirty = false;
        }

        OnRefreshed?.Invoke();
    }

    /// <summary>
    /// Gets all discovered concrete subtypes of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The base type.</typeparam>
    /// <returns>A list of matching concrete types.</returns>
    public static IReadOnlyList<Type> GetSubTypesOf<T>()
    {
        EnsureFresh();
        int keyId = TypeIdentityRegistry.GetOrAddRuntimeTypeId(typeof(T));
        if (s_subclassCache.TryGetValue(keyId, out Type[]? set)) return set;
        return [];
    }

    /// <summary>
    /// Gets all discovered concrete types implementing <typeparamref name="TInterface"/>.
    /// </summary>
    /// <typeparam name="TInterface">The target interface type.</typeparam>
    /// <returns>A list of matching concrete types.</returns>
    public static IReadOnlyList<Type> GetTypesImplementing<TInterface>()
    {
        EnsureFresh();
        int keyId = TypeIdentityRegistry.GetOrAddRuntimeTypeId(typeof(TInterface));
        if (s_interfaceCache.TryGetValue(keyId, out Type[]? set)) return set;
        return [];
    }

    /// <summary>
    /// Gets all discovered concrete types annotated with <typeparamref name="TAttr"/>.
    /// </summary>
    /// <typeparam name="TAttr">The attribute type.</typeparam>
    /// <returns>A list of matching concrete types.</returns>
    public static IReadOnlyList<Type> GetTypesWithAttribute<TAttr>() where TAttr : Attribute
    {
        EnsureFresh();
        int keyId = TypeIdentityRegistry.GetOrAddRuntimeTypeId(typeof(TAttr));
        if (s_attributeCache.TryGetValue(keyId, out Type[]? set)) return set;
        return [];
    }

    private static void EnsureFresh()
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Length != Volatile.Read(ref s_lastAssemblyCount))
            s_isDirty = true;

        if (!s_isDirty)
            return;

        lock (CACHE_SYNC)
        {
            if (s_isDirty)
                Refresh();
        }
    }

    private static void AddToIndex(Dictionary<int, HashSet<Type>> index, Type keyType, Type valueType)
    {
        int keyId = TypeIdentityRegistry.GetOrAddRuntimeTypeId(keyType);
        if (!index.TryGetValue(keyId, out HashSet<Type>? set))
        {
            set = new HashSet<Type>();
            index[keyId] = set;
        }

        set.Add(valueType);
    }

    private static Dictionary<int, Type[]> FreezeIndex(Dictionary<int, HashSet<Type>> index)
    {
        var frozen = new Dictionary<int, Type[]>(index.Count);
        foreach (var pair in index)
        {
            frozen[pair.Key] = pair.Value.ToArray();
        }

        return frozen;
    }
}
