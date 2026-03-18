using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Inno.Core.Reflection;

/// <summary>
/// Provides cached type discovery for Inno namespaces, including subtype, interface, and attribute lookups.
/// </summary>
public static class TypeCacheManager
{
    private const string C_INNO_NAMESPACE = "Inno";

    private static ConditionalWeakTable<Type, WeakTypeSet> s_subclassCache = new();
    private static ConditionalWeakTable<Type, WeakTypeSet> s_interfaceCache = new();
    private static ConditionalWeakTable<Type, WeakTypeSet> s_attributeCache = new();

    private static readonly Lock CACHE_SYNC = new();
    private static volatile bool isDirty = false;
    private static int lastAssemblyCount = -1;

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
            isDirty = true;
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

        var subclassCache = new ConditionalWeakTable<Type, WeakTypeSet>();
        var interfaceCache = new ConditionalWeakTable<Type, WeakTypeSet>();
        var attributeCache = new ConditionalWeakTable<Type, WeakTypeSet>();

        foreach (var type in allTypes)
        {
            if (type.IsAbstract) continue;

            // Index by base type
            var baseType = type.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                GetOrCreateWeakSet(subclassCache, baseType).Add(type);
                baseType = baseType.BaseType;
            }

            // Index by interfaces
            foreach (var iface in type.GetInterfaces())
            {
                GetOrCreateWeakSet(interfaceCache, iface).Add(type);
            }

            // Index by attributes
            foreach (var attr in type.GetCustomAttributes(inherit: true))
            {
                var attrType = attr.GetType();
                GetOrCreateWeakSet(attributeCache, attrType).Add(type);
            }
        }

        lock (CACHE_SYNC)
        {
            s_subclassCache = subclassCache;
            s_interfaceCache = interfaceCache;
            s_attributeCache = attributeCache;
            lastAssemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;
            isDirty = false;
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
        if (s_subclassCache.TryGetValue(typeof(T), out var set)) return set.GetAliveTypes();
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
        if (s_interfaceCache.TryGetValue(typeof(TInterface), out var set)) return set.GetAliveTypes();
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
        if (s_attributeCache.TryGetValue(typeof(TAttr), out var set)) return set.GetAliveTypes();
        return [];
    }

    private static void EnsureFresh()
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Length != Volatile.Read(ref lastAssemblyCount))
            isDirty = true;

        if (!isDirty)
            return;

        lock (CACHE_SYNC)
        {
            if (isDirty)
                Refresh();
        }
    }

    private static WeakTypeSet GetOrCreateWeakSet(ConditionalWeakTable<Type, WeakTypeSet> table, Type key)
    {
        if (table.TryGetValue(key, out var set))
            return set;

        set = new WeakTypeSet();
        try
        {
            table.Add(key, set);
            return set;
        }
        catch (ArgumentException)
        {
            return table.GetValue(key, static _ => new WeakTypeSet());
        }
    }

    private sealed class WeakTypeSet
    {
        private readonly object m_syncRoot = new();
        private readonly List<WeakReference<Type>> m_items = new();

        public void Add(Type type)
        {
            lock (m_syncRoot)
            {
                m_items.Add(new WeakReference<Type>(type));
            }
        }

        public IReadOnlyList<Type> GetAliveTypes()
        {
            lock (m_syncRoot)
            {
                var result = new List<Type>(m_items.Count);
                var write = 0;

                for (var i = 0; i < m_items.Count; i++)
                {
                    var wr = m_items[i];
                    if (!wr.TryGetTarget(out var target))
                        continue;

                    result.Add(target);
                    m_items[write++] = wr;
                }

                if (write < m_items.Count)
                    m_items.RemoveRange(write, m_items.Count - write);

                return result;
            }
        }
    }
}
