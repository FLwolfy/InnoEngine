using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Core.Reflection;

/// <summary>
/// Internal query registry for subtype/interface/attribute lookups.
/// </summary>
internal sealed class TypeQueryRegistry
{
    private Dictionary<int, Type[]> m_subclassCache = [];
    private Dictionary<int, Type[]> m_interfaceCache = [];
    private Dictionary<int, Type[]> m_attributeCache = [];

    public void Rebuild(IEnumerable<Type> concreteTypes, TypeIdentityRegistry typeIdentityRegistry)
    {
        ArgumentNullException.ThrowIfNull(concreteTypes);
        ArgumentNullException.ThrowIfNull(typeIdentityRegistry);

        var subclassSets = new Dictionary<int, HashSet<Type>>();
        var interfaceSets = new Dictionary<int, HashSet<Type>>();
        var attributeSets = new Dictionary<int, HashSet<Type>>();

        foreach (Type type in concreteTypes)
        {
            if (type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            Type? baseType = type.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                AddToIndex(subclassSets, baseType, type, typeIdentityRegistry);
                baseType = baseType.BaseType;
            }

            foreach (Type iface in type.GetInterfaces())
            {
                AddToIndex(interfaceSets, iface, type, typeIdentityRegistry);
            }

            foreach (Attribute attr in type.GetCustomAttributes(inherit: true))
            {
                AddToIndex(attributeSets, attr.GetType(), type, typeIdentityRegistry);
            }
        }

        m_subclassCache = FreezeIndex(subclassSets);
        m_interfaceCache = FreezeIndex(interfaceSets);
        m_attributeCache = FreezeIndex(attributeSets);
    }

    public IReadOnlyList<Type> GetSubTypesOf<T>(TypeIdentityRegistry typeIdentityRegistry)
    {
        int keyId = typeIdentityRegistry.GetOrAddRuntimeTypeId(typeof(T));
        return m_subclassCache.TryGetValue(keyId, out Type[]? set) ? set : [];
    }

    public IReadOnlyList<Type> GetTypesImplementing<TInterface>(TypeIdentityRegistry typeIdentityRegistry)
    {
        int keyId = typeIdentityRegistry.GetOrAddRuntimeTypeId(typeof(TInterface));
        return m_interfaceCache.TryGetValue(keyId, out Type[]? set) ? set : [];
    }

    public IReadOnlyList<Type> GetTypesWithAttribute<TAttr>(TypeIdentityRegistry typeIdentityRegistry)
        where TAttr : Attribute
    {
        int keyId = typeIdentityRegistry.GetOrAddRuntimeTypeId(typeof(TAttr));
        return m_attributeCache.TryGetValue(keyId, out Type[]? set) ? set : [];
    }

    private static void AddToIndex(
        Dictionary<int, HashSet<Type>> index,
        Type keyType,
        Type valueType,
        TypeIdentityRegistry typeIdentityRegistry)
    {
        int keyId = typeIdentityRegistry.GetOrAddRuntimeTypeId(keyType);
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
        foreach ((int key, HashSet<Type> value) in index)
        {
            frozen[key] = [.. value];
        }

        return frozen;
    }
}
