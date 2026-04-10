using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Inno.Core.Serialization;

internal sealed class MapCodec<TMap> : SerializationCodec<TMap>
{
    public override bool CanHandleType(Type declaredType)
        => TryGetDictionaryTypes(declaredType, out _, out _);

    public override object? OnSerialize(in SerializeContext context, TMap value)
    {
        if (!TryEnumerateDictionaryEntries(value!, typeof(TMap), out List<KeyValuePair<object?, object?>> entries))
            throw new InvalidOperationException($"Map-like type '{typeof(TMap).FullName}' cannot be enumerated.");

        if (!TryGetDictionaryTypes(typeof(TMap), out Type keyType, out Type valueType))
            throw new InvalidOperationException($"Type '{typeof(TMap).FullName}' is not a supported map type.");

        var map = new Dictionary<object, object?>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            KeyValuePair<object?, object?> entry = entries[i];
            map[context.Serialize(entry.Key, keyType)!] = context.Serialize(entry.Value, valueType);
        }

        return map;
    }

    public override TMap OnDeserialize(in DeserializeContext context, object? node)
    {
        if (!TryReadMapEntries(node, out List<KeyValuePair<object?, object?>> entries))
            throw new InvalidOperationException($"Map node must be IDictionary or key-value sequence. Got: {node?.GetType().FullName ?? "null"}");

        return (TMap)BuildMapFromNodes(typeof(TMap), entries, context);
    }

    private static bool TryGetDictionaryTypes(Type type, out Type keyType, out Type valueType)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        foreach (Type candidate in EnumerateSelfAndInterfaces(t))
        {
            if (!candidate.IsGenericType)
                continue;

            Type def = candidate.GetGenericTypeDefinition();
            if (def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
            {
                Type[] args = candidate.GetGenericArguments();
                keyType = args[0];
                valueType = args[1];
                return true;
            }

            if (def != typeof(IEnumerable<>))
                continue;

            Type elem = candidate.GetGenericArguments()[0];
            if (!elem.IsGenericType || elem.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
                continue;

            Type[] kvArgs = elem.GetGenericArguments();
            keyType = kvArgs[0];
            valueType = kvArgs[1];
            return true;
        }

        keyType = null!;
        valueType = null!;
        return false;
    }

    private static bool TryEnumerateDictionaryEntries(object dictionaryLike, Type dictionaryLikeType, out List<KeyValuePair<object?, object?>> entries)
    {
        entries = new List<KeyValuePair<object?, object?>>();
        if (!TryGetDictionaryTypes(dictionaryLikeType, out _, out _))
            return false;

        if (dictionaryLike is IDictionary dict)
        {
            entries.Capacity = dict.Count;
            foreach (DictionaryEntry entry in dict)
                entries.Add(new KeyValuePair<object?, object?>(entry.Key, entry.Value));
            return true;
        }

        if (dictionaryLike is not IEnumerable enumerable)
            return false;

        foreach (object? item in enumerable)
        {
            if (item == null)
                return false;

            Type itemType = item.GetType();
            if (!itemType.IsGenericType || itemType.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
                return false;

            object? key = itemType.GetProperty("Key")!.GetValue(item);
            object? value = itemType.GetProperty("Value")!.GetValue(item);
            entries.Add(new KeyValuePair<object?, object?>(key, value));
        }

        return true;
    }

    private static bool TryReadMapEntries(object? raw, out List<KeyValuePair<object?, object?>> entries)
    {
        if (raw is IDictionary dict)
        {
            entries = new List<KeyValuePair<object?, object?>>(dict.Count);
            foreach (DictionaryEntry entry in dict)
                entries.Add(new KeyValuePair<object?, object?>(entry.Key, entry.Value));
            return true;
        }

        if (raw is not IEnumerable enumerable)
        {
            entries = new List<KeyValuePair<object?, object?>>();
            return false;
        }

        entries = new List<KeyValuePair<object?, object?>>();
        foreach (object? item in enumerable)
        {
            if (item == null)
                return false;

            Type itemType = item.GetType();
            if (!itemType.IsGenericType || itemType.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
                return false;

            object? key = itemType.GetProperty("Key")!.GetValue(item);
            object? value = itemType.GetProperty("Value")!.GetValue(item);
            entries.Add(new KeyValuePair<object?, object?>(key, value));
        }

        return true;
    }

    private static object BuildMapFromNodes(Type targetType, List<KeyValuePair<object?, object?>> entries, in DeserializeContext context)
    {
        if (!TryGetDictionaryTypes(targetType, out Type keyType, out Type valueType))
            throw new InvalidOperationException($"Type '{targetType.FullName}' is not a map-like type.");

        var restoredEntries = new KeyValuePair<object?, object?>[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            KeyValuePair<object?, object?> entry = entries[i];
            restoredEntries[i] = new KeyValuePair<object?, object?>(
                context.Deserialize(entry.Key, keyType),
                context.Deserialize(entry.Value, valueType));
        }

        Type dictionaryType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
        object dictionary = BuildTypedDictionary(keyType, valueType, restoredEntries);
        if (targetType.IsAssignableFrom(dictionaryType))
            return dictionary;

        Type kvType = typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType);
        object kvList = BuildTypedKeyValueList(kvType, restoredEntries);
        Type kvListType = kvList.GetType();
        ConstructorInfo? constructor = FindSingleArgConstructor(
            targetType,
            kvListType,
            typeof(IEnumerable<>).MakeGenericType(kvType));
        if (constructor != null)
            return constructor.Invoke(new[] { kvList });

        MethodInfo? staticFactory = targetType
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m =>
                (m.Name == "CreateRange" || m.Name == "Create") &&
                targetType.IsAssignableFrom(m.ReturnType) &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.IsAssignableFrom(kvListType));
        if (staticFactory != null)
            return staticFactory.Invoke(null, new[] { kvList })!;

        if (!targetType.IsAbstract && ResolveMapAddMethod(targetType, keyType, valueType) is MethodInfo addMethod)
        {
            object instance = Activator.CreateInstance(targetType, nonPublic: true)!;
            for (int i = 0; i < restoredEntries.Length; i++)
            {
                KeyValuePair<object?, object?> entry = restoredEntries[i];
                addMethod.Invoke(instance, new[] { entry.Key, entry.Value });
            }

            return instance;
        }

        throw new InvalidOperationException(
            $"Cannot construct map type '{targetType.FullName}'. Provide ctor(IEnumerable<KeyValuePair<K,V>>), static CreateRange/Create, or Add(K,V).");
    }

    private static MethodInfo? ResolveMapAddMethod(Type targetType, Type keyType, Type valueType)
    {
        MethodInfo? direct = targetType.GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { keyType, valueType },
            modifiers: null);
        if (direct != null)
            return direct;

        Type iface = typeof(IDictionary<,>).MakeGenericType(keyType, valueType);
        return iface.IsAssignableFrom(targetType) ? iface.GetMethod("Add", new[] { keyType, valueType }) : null;
    }

    private static ConstructorInfo? FindSingleArgConstructor(Type targetType, params Type[] candidateArgTypes)
    {
        ConstructorInfo[] constructors = targetType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < constructors.Length; i++)
        {
            ConstructorInfo ctor = constructors[i];
            ParameterInfo[] parameters = ctor.GetParameters();
            if (parameters.Length != 1)
                continue;

            Type paramType = parameters[0].ParameterType;
            for (int c = 0; c < candidateArgTypes.Length; c++)
            {
                if (paramType.IsAssignableFrom(candidateArgTypes[c]))
                    return ctor;
            }
        }

        return null;
    }

    private static object BuildTypedDictionary(Type keyType, Type valueType, KeyValuePair<object?, object?>[] entries)
    {
        Type dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
        IDictionary dict = (IDictionary)Activator.CreateInstance(dictType, nonPublic: true)!;
        for (int i = 0; i < entries.Length; i++)
        {
            KeyValuePair<object?, object?> entry = entries[i];
            dict.Add(entry.Key!, entry.Value);
        }

        return dict;
    }

    private static object BuildTypedKeyValueList(Type kvType, KeyValuePair<object?, object?>[] entries)
    {
        Type listType = typeof(List<>).MakeGenericType(kvType);
        IList list = (IList)Activator.CreateInstance(listType, nonPublic: true)!;
        for (int i = 0; i < entries.Length; i++)
        {
            KeyValuePair<object?, object?> entry = entries[i];
            list.Add(Activator.CreateInstance(kvType, entry.Key, entry.Value)!);
        }

        return list;
    }

    private static IEnumerable<Type> EnumerateSelfAndInterfaces(Type type)
    {
        yield return type;
        Type[] interfaces = type.GetInterfaces();
        for (int i = 0; i < interfaces.Length; i++)
            yield return interfaces[i];
    }
}
