using System;

namespace Inno.Core.Serialization;

internal sealed class SerializablePrimitiveCodec<T> : SerializationCodec<T>
{
    public override bool CanHandleType(Type declaredType)
        => PrimitiveSerializer.IsPrimitiveType(declaredType) &&
           (Nullable.GetUnderlyingType(declaredType) ?? declaredType) == typeof(T);

    public override object? OnSerialize(in SerializeContext context, T value)
        => PrimitiveSerializer.Serialize(value!, typeof(T));

    public override T OnDeserialize(in DeserializeContext context, object? node)
        => (T)PrimitiveSerializer.Deserialize(node, typeof(T))!;
}
