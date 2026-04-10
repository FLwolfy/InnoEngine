using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Inno.Core.Serialization;

internal sealed class CollectionCodec<TCollection> : SerializationCodec<TCollection>
{
    public override bool CanHandleType(Type declaredType)
        => TryGetListElementType(declaredType, out _);

    public override object? OnSerialize(in SerializeContext context, TCollection value)
    {
        if (value is not IEnumerable enumerable)
            throw new InvalidOperationException($"Type '{typeof(TCollection).FullName}' is not enumerable.");

        if (!TryGetListElementType(typeof(TCollection), out Type elementType))
            throw new InvalidOperationException($"Type '{typeof(TCollection).FullName}' is not a supported sequence type.");

        var result = new List<object?>();
        foreach (object? item in enumerable)
            result.Add(context.Serialize(item, elementType));

        return result;
    }

    public override TCollection OnDeserialize(in DeserializeContext context, object? node)
    {
        if (node is not IReadOnlyList<object?> list)
            throw new InvalidOperationException($"Sequence node must be IReadOnlyList<object?>. Got {node?.GetType().FullName ?? "null"}");

        return (TCollection)BuildSequenceFromNodes(typeof(TCollection), list, context);
    }

    private static bool TryGetListElementType(Type type, out Type elementType)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string) || t.IsArray)
        {
            elementType = null!;
            return false;
        }

        foreach (Type candidate in EnumerateSelfAndInterfaces(t))
        {
            if (!candidate.IsGenericType || candidate.GetGenericTypeDefinition() != typeof(IEnumerable<>))
                continue;

            Type elem = candidate.GetGenericArguments()[0];
            if (elem == typeof(char) && t == typeof(string))
                continue;

            if (elem.IsGenericType && elem.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                continue;

            elementType = elem;
            return true;
        }

        elementType = null!;
        return false;
    }

    private static object BuildSequenceFromNodes(Type targetType, IReadOnlyList<object?> nodes, in DeserializeContext context)
    {
        if (!TryGetListElementType(targetType, out Type elementType))
            throw new InvalidOperationException($"Type '{targetType.FullName}' is not a sequence-like type.");

        object?[] items = new object?[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
            items[i] = context.Deserialize(nodes[i], elementType);

        Type listType = typeof(List<>).MakeGenericType(elementType);
        object list = BuildTypedList(elementType, items);
        if (targetType.IsAssignableFrom(listType))
            return list;

        ConstructorInfo? constructor = FindSingleArgConstructor(
            targetType,
            listType,
            typeof(IEnumerable<>).MakeGenericType(elementType));
        if (constructor != null)
            return constructor.Invoke(new[] { list });

        MethodInfo? staticFactory = targetType
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m =>
                (m.Name == "CreateRange" || m.Name == "Create") &&
                targetType.IsAssignableFrom(m.ReturnType) &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.IsAssignableFrom(listType));
        if (staticFactory != null)
            return staticFactory.Invoke(null, new[] { list })!;

        if (!targetType.IsAbstract && ResolveSequenceAddMethod(targetType, elementType) is MethodInfo addMethod)
        {
            object instance = Activator.CreateInstance(targetType, nonPublic: true)!;
            for (int i = 0; i < items.Length; i++)
                addMethod.Invoke(instance, new[] { items[i] });
            return instance;
        }

        throw new InvalidOperationException(
            $"Cannot construct sequence type '{targetType.FullName}'. Provide ctor(IEnumerable<T>), static CreateRange/Create, or Add(T).");
    }

    private static MethodInfo? ResolveSequenceAddMethod(Type targetType, Type elementType)
    {
        MethodInfo? direct = targetType.GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { elementType },
            modifiers: null);
        if (direct != null)
            return direct;

        Type iface = typeof(ICollection<>).MakeGenericType(elementType);
        return iface.IsAssignableFrom(targetType) ? iface.GetMethod("Add", new[] { elementType }) : null;
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

    private static object BuildTypedList(Type elementType, object?[] items)
    {
        Type listType = typeof(List<>).MakeGenericType(elementType);
        IList list = (IList)Activator.CreateInstance(listType, nonPublic: true)!;
        for (int i = 0; i < items.Length; i++)
            list.Add(items[i]);

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
