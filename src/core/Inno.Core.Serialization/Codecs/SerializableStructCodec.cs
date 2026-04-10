using System;

namespace Inno.Core.Serialization;

internal sealed class SerializableStructCodec<T> : SerializationCodec<T> where T : struct
{
    public override bool CanHandleType(Type declaredType)
        => StructSerializer.IsStructPayloadType(declaredType) &&
           (Nullable.GetUnderlyingType(declaredType) ?? declaredType) == typeof(T);

    public override object? OnSerialize(in SerializeContext context, T value)
    {
        SerializeContext captured = context;
        return StructSerializer.Serialize(
            value,
            typeof(T),
            (nestedValue, nestedType) => captured.Serialize(nestedValue, nestedType));
    }

    public override T OnDeserialize(in DeserializeContext context, object? node)
    {
        if (node == null)
            throw new InvalidOperationException($"Struct node for '{typeof(T).FullName}' cannot be null.");

        DeserializeContext captured = context;
        return (T)StructSerializer.Deserialize(
            node,
            typeof(T),
            (nestedNode, nestedType) => captured.Deserialize(nestedNode, nestedType));
    }
}
