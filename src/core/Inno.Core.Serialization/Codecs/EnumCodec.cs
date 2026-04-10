using System;

namespace Inno.Core.Serialization;

internal sealed class EnumCodec<TEnum> : SerializationCodec<TEnum> where TEnum : struct, Enum
{
    public override bool CanHandleType(Type declaredType)
        => (Nullable.GetUnderlyingType(declaredType) ?? declaredType) == typeof(TEnum);

    public override object? OnSerialize(in SerializeContext context, TEnum value) => Convert.ToInt64(value);

    public override TEnum OnDeserialize(in DeserializeContext context, object? node)
        => (TEnum)Enum.ToObject(typeof(TEnum), Convert.ToInt64(node));
}
