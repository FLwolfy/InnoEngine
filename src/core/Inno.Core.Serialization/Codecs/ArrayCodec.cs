using System;
using System.Collections.Generic;

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
        if (node is not IReadOnlyList<object?> list)
            throw new InvalidOperationException("Array node must be IReadOnlyList<object?>.");

        var arr = new TElement[list.Count];
        for (int i = 0; i < list.Count; i++)
            arr[i] = (TElement)context.Deserialize(list[i], typeof(TElement))!;

        return arr;
    }
}
