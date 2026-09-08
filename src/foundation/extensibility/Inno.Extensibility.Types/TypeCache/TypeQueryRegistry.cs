using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Extensibility.Types;

/// <summary>
/// Internal query registry for subtype/interface/attribute lookups.
/// </summary>
internal sealed class TypeQueryRegistry
{
    private Dictionary<int, IReadOnlyList<TypeRef>> m_subclassCache = [];
    private Dictionary<int, IReadOnlyList<TypeRef>> m_interfaceCache = [];
    private Dictionary<int, IReadOnlyList<TypeRef>> m_attributeCache = [];

    /// <summary>
    /// Builds a complete candidate lookup from the supplied types before replacing current state.
    /// </summary>
    /// <param name="concreteTypes">
    /// The concrete types consumed by rebuild; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="typeIdentityRegistry">
    /// The type identity registry consumed by rebuild; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
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

        m_subclassCache = FreezeIndex(subclassSets, typeIdentityRegistry);
        m_interfaceCache = FreezeIndex(interfaceSets, typeIdentityRegistry);
        m_attributeCache = FreezeIndex(attributeSets, typeIdentityRegistry);
    }

    /// <summary>
    /// Retrieves the requested sub types of value from current authoritative state.
    /// </summary>
    /// <typeparam name="T">
    /// The caller-selected t type whose declared constraints are enforced by this operation.
    /// </typeparam>
    /// <param name="typeIdentityRegistry">
    /// The type identity registry consumed by get sub types of; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// An immutable snapshot of the values selected by the operation.
    /// </returns>
    public IReadOnlyList<TypeRef> GetSubTypesOf<T>(TypeIdentityRegistry typeIdentityRegistry)
    {
        return typeIdentityRegistry.TryGetRuntimeTypeId(typeof(T), out int keyId) &&
               m_subclassCache.TryGetValue(keyId, out IReadOnlyList<TypeRef>? set)
            ? set
            : [];
    }

    /// <summary>
    /// Retrieves the requested types implementing value from current authoritative state.
    /// </summary>
    /// <typeparam name="TInterface">
    /// The caller-selected tinterface type whose declared constraints are enforced by this operation.
    /// </typeparam>
    /// <param name="typeIdentityRegistry">
    /// The type identity registry consumed by get types implementing; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// An immutable snapshot of the values selected by the operation.
    /// </returns>
    public IReadOnlyList<TypeRef> GetTypesImplementing<TInterface>(TypeIdentityRegistry typeIdentityRegistry)
    {
        return typeIdentityRegistry.TryGetRuntimeTypeId(typeof(TInterface), out int keyId) &&
               m_interfaceCache.TryGetValue(keyId, out IReadOnlyList<TypeRef>? set)
            ? set
            : [];
    }

    /// <summary>
    /// Retrieves the requested types with attribute value from current authoritative state.
    /// </summary>
    /// <typeparam name="TAttr">
    /// The caller-selected tattr type whose declared constraints are enforced by this operation.
    /// </typeparam>
    /// <param name="typeIdentityRegistry">
    /// The type identity registry consumed by get types with attribute; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// An immutable snapshot of the values selected by the operation.
    /// </returns>
    public IReadOnlyList<TypeRef> GetTypesWithAttribute<TAttr>(TypeIdentityRegistry typeIdentityRegistry)
        where TAttr : Attribute
    {
        return typeIdentityRegistry.TryGetRuntimeTypeId(typeof(TAttr), out int keyId) &&
               m_attributeCache.TryGetValue(keyId, out IReadOnlyList<TypeRef>? set)
            ? set
            : [];
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

    private static Dictionary<int, IReadOnlyList<TypeRef>> FreezeIndex(
        Dictionary<int, HashSet<Type>> index,
        TypeIdentityRegistry typeIdentityRegistry)
    {
        var frozen = new Dictionary<int, IReadOnlyList<TypeRef>>(index.Count);
        foreach ((int key, HashSet<Type> value) in index)
        {
            frozen[key] = Array.AsReadOnly(value
                .Select(typeIdentityRegistry.GetTypeRef)
                .ToArray());
        }

        return frozen;
    }
}
