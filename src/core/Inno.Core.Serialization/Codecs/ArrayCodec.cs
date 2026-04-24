using System;
using System.Collections.Generic;
using System.Collections;

namespace Inno.Core.Serialization;

internal sealed class ArrayCodec<TElement> : SerializationCodec<TElement[]>
{
    public override bool CanHandleType(Type declaredType)
        => (Nullable.GetUnderlyingType(declaredType) ?? declaredType) == typeof(TElement[]);

    public override object? OnSerialize(in SerializeContext context, TElement[] value)
    {
        var list = new List<object?>(value.Length);
        for (int i = 0; i < value.Length; i++)
            list.Add(context.Serialize(value[i], typeof(TElement)));

        return list;
    }

    public override TElement[] OnDeserialize(in DeserializeContext context, object? node)
    {
        IReadOnlyList<object?> list = node switch
        {
            IReadOnlyList<object?> direct => direct,
            Array array => ToObjectList(array),
            IEnumerable enumerable => ToObjectList(enumerable),
            _ => throw new InvalidOperationException("Array node must be enumerable.")
        };

        var arr = new TElement[list.Count];
        for (int i = 0; i < list.Count; i++)
            arr[i] = (TElement)context.Deserialize(list[i], typeof(TElement))!;

        return arr;
    }

    private static IReadOnlyList<object?> ToObjectList(IEnumerable source)
    {
        var list = new List<object?>();
        foreach (object? item in source)
            list.Add(item);

        return list;
    }
}
