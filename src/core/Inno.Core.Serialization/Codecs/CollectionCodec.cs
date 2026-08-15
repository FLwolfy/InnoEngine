using System;
using System.Collections;
using System.Collections.Generic;

namespace Inno.Core.Serialization;

internal sealed class CollectionCodec<TCollection> : SerializationCodec<TCollection>
{
    public override bool CanHandleType(Type declaredType)
    {
        Type normalizedType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        return !normalizedType.IsArray &&
            CollectionTypeUtility.TryGetSequenceElementType(normalizedType, out _);
    }

    public override object? OnSerialize(in SerializeContext context, TCollection value)
    {
        if (value is not IEnumerable enumerable)
        {
            throw new InvalidOperationException($"Type '{typeof(TCollection).FullName}' is not enumerable.");
        }

        if (!CollectionTypeUtility.TryGetSequenceElementType(typeof(TCollection), out Type elementType))
        {
            throw new InvalidOperationException($"Type '{typeof(TCollection).FullName}' is not a supported sequence type.");
        }

        var result = new List<object?>();
        foreach (object? item in enumerable)
        {
            result.Add(context.Serialize(item, elementType));
        }

        return result;
    }

    public override TCollection OnDeserialize(in DeserializeContext context, object? node)
    {
        IReadOnlyList<object?> nodes = node switch
        {
            IReadOnlyList<object?> direct => direct,
            IEnumerable enumerable => CollectionTypeUtility.EnumerateSequence(enumerable),
            _ => throw new InvalidOperationException(
                $"Sequence node must be enumerable. Got {node?.GetType().FullName ?? "null"}")
        };

        if (!CollectionTypeUtility.TryGetSequenceElementType(typeof(TCollection), out Type elementType))
        {
            throw new InvalidOperationException($"Type '{typeof(TCollection).FullName}' is not a supported sequence type.");
        }

        var values = new object?[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            values[i] = context.Deserialize(nodes[i], elementType);
        }

        return (TCollection)CollectionTypeUtility.BuildSequence(
            typeof(TCollection),
            elementType,
            values);
    }
}
