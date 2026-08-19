using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Inno.Editor.Scene.Inspection;

/// <summary>
/// Provides shared collection shape and copy construction rules for editor collection drawers.
/// </summary>
internal static class EditorCollectionUtility
{
    internal static bool TryGetSequenceElementType(Type type, out Type elementType)
    {
        Type normalizedType = Nullable.GetUnderlyingType(type) ?? type;
        if (normalizedType == typeof(string))
        {
            elementType = null!;
            return false;
        }

        if (normalizedType.IsArray)
        {
            elementType = normalizedType.GetElementType()!;
            return normalizedType.GetArrayRank() == 1;
        }

        foreach (Type candidate in EnumerateSelfAndInterfaces(normalizedType))
        {
            if (!candidate.IsGenericType || candidate.GetGenericTypeDefinition() != typeof(IEnumerable<>))
            {
                continue;
            }

            Type candidateElementType = candidate.GetGenericArguments()[0];
            if (candidateElementType.IsGenericType &&
                candidateElementType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                continue;
            }

            elementType = candidateElementType;
            return true;
        }

        elementType = null!;
        return false;
    }

    internal static bool TryGetMapTypes(Type type, out Type keyType, out Type valueType)
    {
        Type normalizedType = Nullable.GetUnderlyingType(type) ?? type;
        foreach (Type candidate in EnumerateSelfAndInterfaces(normalizedType))
        {
            if (!candidate.IsGenericType)
            {
                continue;
            }

            Type definition = candidate.GetGenericTypeDefinition();
            if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
            {
                Type[] arguments = candidate.GetGenericArguments();
                keyType = arguments[0];
                valueType = arguments[1];
                return true;
            }

            if (definition != typeof(IEnumerable<>))
            {
                continue;
            }

            Type entryType = candidate.GetGenericArguments()[0];
            if (!entryType.IsGenericType || entryType.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
            {
                continue;
            }

            Type[] entryArguments = entryType.GetGenericArguments();
            keyType = entryArguments[0];
            valueType = entryArguments[1];
            return true;
        }

        keyType = null!;
        valueType = null!;
        return false;
    }

    internal static List<object?> EnumerateSequence(object? sequence)
    {
        var values = new List<object?>();
        if (sequence is not IEnumerable enumerable)
        {
            return values;
        }

        foreach (object? value in enumerable)
        {
            values.Add(value);
        }

        return values;
    }

    internal static bool TryEnumerateMap(
        object? map,
        Type mapType,
        out List<KeyValuePair<object?, object?>> entries)
    {
        entries = [];
        if (!TryGetMapTypes(mapType, out _, out _))
        {
            return false;
        }

        if (map is IDictionary dictionary)
        {
            entries.Capacity = dictionary.Count;
            foreach (DictionaryEntry entry in dictionary)
            {
                entries.Add(new KeyValuePair<object?, object?>(entry.Key, entry.Value));
            }

            return true;
        }

        if (map is not IEnumerable enumerable)
        {
            return false;
        }

        foreach (object? item in enumerable)
        {
            if (item is null)
            {
                return false;
            }

            Type itemType = item.GetType();
            if (!itemType.IsGenericType || itemType.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
            {
                return false;
            }

            object? key = itemType.GetProperty("Key")!.GetValue(item);
            object? value = itemType.GetProperty("Value")!.GetValue(item);
            entries.Add(new KeyValuePair<object?, object?>(key, value));
        }

        return true;
    }

    internal static object BuildSequence(
        Type targetType,
        Type elementType,
        IReadOnlyList<object?> values)
    {
        if (targetType.IsArray)
        {
            Array array = Array.CreateInstance(elementType, values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                array.SetValue(values[i], i);
            }

            return array;
        }

        Type listType = typeof(List<>).MakeGenericType(elementType);
        object list = BuildTypedList(elementType, values);
        if (targetType.IsAssignableFrom(listType))
        {
            return list;
        }

        ConstructorInfo? constructor = FindSingleArgumentConstructor(
            targetType,
            listType,
            typeof(IEnumerable<>).MakeGenericType(elementType));
        if (constructor is not null)
        {
            return constructor.Invoke([list]);
        }

        MethodInfo? factory = FindFactory(targetType, listType);
        if (factory is not null)
        {
            return factory.Invoke(null, [list])!;
        }

        if (!targetType.IsAbstract && ResolveSequenceAddMethod(targetType, elementType) is MethodInfo addMethod)
        {
            object instance = Activator.CreateInstance(targetType, nonPublic: true)
                ?? throw new InvalidOperationException($"Cannot create sequence type '{targetType.FullName}'.");
            for (int i = 0; i < values.Count; i++)
            {
                addMethod.Invoke(instance, [values[i]]);
            }

            return instance;
        }

        throw new InvalidOperationException(
            $"Cannot construct sequence type '{targetType.FullName}'. Provide ctor(IEnumerable<T>), static CreateRange/Create, or Add(T).");
    }

    internal static object BuildMap(
        Type targetType,
        Type keyType,
        Type valueType,
        IReadOnlyList<KeyValuePair<object?, object?>> entries)
    {
        Type dictionaryType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
        object dictionary = BuildTypedDictionary(dictionaryType, entries);
        if (targetType.IsAssignableFrom(dictionaryType))
        {
            return dictionary;
        }

        Type pairType = typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType);
        object pairList = BuildTypedKeyValueList(pairType, entries);
        Type pairListType = pairList.GetType();
        ConstructorInfo? constructor = FindSingleArgumentConstructor(
            targetType,
            pairListType,
            typeof(IEnumerable<>).MakeGenericType(pairType));
        if (constructor is not null)
        {
            return constructor.Invoke([pairList]);
        }

        MethodInfo? factory = FindFactory(targetType, pairListType);
        if (factory is not null)
        {
            return factory.Invoke(null, [pairList])!;
        }

        if (!targetType.IsAbstract && ResolveMapAddMethod(targetType, keyType, valueType) is MethodInfo addMethod)
        {
            object instance = Activator.CreateInstance(targetType, nonPublic: true)
                ?? throw new InvalidOperationException($"Cannot create map type '{targetType.FullName}'.");
            for (int i = 0; i < entries.Count; i++)
            {
                KeyValuePair<object?, object?> entry = entries[i];
                addMethod.Invoke(instance, [entry.Key, entry.Value]);
            }

            return instance;
        }

        throw new InvalidOperationException(
            $"Cannot construct map type '{targetType.FullName}'. Provide ctor(IEnumerable<KeyValuePair<K,V>>), static CreateRange/Create, or Add(K,V).");
    }

    private static object BuildTypedList(Type elementType, IReadOnlyList<object?> values)
    {
        Type listType = typeof(List<>).MakeGenericType(elementType);
        IList list = (IList)Activator.CreateInstance(listType)!;
        for (int i = 0; i < values.Count; i++)
        {
            list.Add(values[i]);
        }

        return list;
    }

    private static object BuildTypedDictionary(
        Type dictionaryType,
        IReadOnlyList<KeyValuePair<object?, object?>> entries)
    {
        IDictionary dictionary = (IDictionary)Activator.CreateInstance(dictionaryType)!;
        for (int i = 0; i < entries.Count; i++)
        {
            KeyValuePair<object?, object?> entry = entries[i];
            dictionary.Add(
                entry.Key ?? throw new InvalidOperationException("Map keys cannot be null."),
                entry.Value);
        }

        return dictionary;
    }

    private static object BuildTypedKeyValueList(
        Type pairType,
        IReadOnlyList<KeyValuePair<object?, object?>> entries)
    {
        Type listType = typeof(List<>).MakeGenericType(pairType);
        IList list = (IList)Activator.CreateInstance(listType)!;
        for (int i = 0; i < entries.Count; i++)
        {
            KeyValuePair<object?, object?> entry = entries[i];
            list.Add(Activator.CreateInstance(pairType, entry.Key, entry.Value)!);
        }

        return list;
    }

    private static ConstructorInfo? FindSingleArgumentConstructor(
        Type targetType,
        params Type[] candidateArgumentTypes)
    {
        ConstructorInfo[] constructors = targetType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < constructors.Length; i++)
        {
            ParameterInfo[] parameters = constructors[i].GetParameters();
            if (parameters.Length != 1)
            {
                continue;
            }

            for (int candidateIndex = 0; candidateIndex < candidateArgumentTypes.Length; candidateIndex++)
            {
                if (parameters[0].ParameterType.IsAssignableFrom(candidateArgumentTypes[candidateIndex]))
                {
                    return constructors[i];
                }
            }
        }

        return null;
    }

    private static MethodInfo? FindFactory(Type targetType, Type argumentType)
    {
        return targetType
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method =>
                (method.Name == "CreateRange" || method.Name == "Create") &&
                targetType.IsAssignableFrom(method.ReturnType) &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType.IsAssignableFrom(argumentType));
    }

    private static MethodInfo? ResolveSequenceAddMethod(Type targetType, Type elementType)
    {
        MethodInfo? direct = targetType.GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [elementType],
            modifiers: null);
        if (direct is not null)
        {
            return direct;
        }

        Type collectionInterface = typeof(ICollection<>).MakeGenericType(elementType);
        return collectionInterface.IsAssignableFrom(targetType)
            ? collectionInterface.GetMethod("Add", [elementType])
            : null;
    }

    private static MethodInfo? ResolveMapAddMethod(Type targetType, Type keyType, Type valueType)
    {
        MethodInfo? direct = targetType.GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [keyType, valueType],
            modifiers: null);
        if (direct is not null)
        {
            return direct;
        }

        Type dictionaryInterface = typeof(IDictionary<,>).MakeGenericType(keyType, valueType);
        return dictionaryInterface.IsAssignableFrom(targetType)
            ? dictionaryInterface.GetMethod("Add", [keyType, valueType])
            : null;
    }

    private static IEnumerable<Type> EnumerateSelfAndInterfaces(Type type)
    {
        yield return type;
        Type[] interfaces = type.GetInterfaces();
        for (int i = 0; i < interfaces.Length; i++)
        {
            yield return interfaces[i];
        }
    }
}
