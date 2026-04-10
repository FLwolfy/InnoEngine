using System;

namespace Inno.Core.Serialization;

internal sealed class SerializableClassCodec<T> : SerializationCodec<T> where T : class, ISerializable
{
    public override bool CanHandleType(Type declaredType)
    {
        Type normalized = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        return ClassSerializer.IsSerializableClassType(normalized) && typeof(T).IsAssignableFrom(normalized);
    }

    public override object? OnSerialize(in SerializeContext context, T value)
        => ClassSerializer.Serialize(value);

    public override T OnDeserialize(in DeserializeContext context, object? node)
        => (T)ClassSerializer.Deserialize(node, typeof(T));
}
