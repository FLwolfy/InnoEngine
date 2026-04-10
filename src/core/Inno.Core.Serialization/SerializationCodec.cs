using System;

namespace Inno.Core.Serialization;

/// <summary>
/// Base non-generic serialization codec contract.
/// </summary>
public interface ISerializationCodec
{
    /// <summary>
    /// Declared target type this codec handles.
    /// </summary>
    Type targetType { get; }

    /// <summary>
    /// Returns true when this codec can handle the provided declared type.
    /// </summary>
    bool CanHandleType(Type declaredType);

    /// <summary>
    /// Serializes a value into a serializable node.
    /// </summary>
    object? OnSerialize(in SerializeContext context, object value);

    /// <summary>
    /// Deserializes a serializable node back into a typed value.
    /// </summary>
    object? OnDeserialize(in DeserializeContext context, object? node);
}

/// <summary>
/// Generic codec base for strongly typed custom serialization.
/// </summary>
/// <typeparam name="T">Target type.</typeparam>
public abstract class SerializationCodec<T> : ISerializationCodec
{
    /// <inheritdoc />
    public Type targetType => typeof(T);

    /// <inheritdoc />
    public abstract bool CanHandleType(Type declaredType);

    /// <inheritdoc />
    object? ISerializationCodec.OnSerialize(in SerializeContext context, object value)
        => OnSerialize(context, (T)value);

    /// <inheritdoc />
    object? ISerializationCodec.OnDeserialize(in DeserializeContext context, object? node)
        => OnDeserialize(context, node);

    /// <summary>
    /// Serializes a strongly typed value into a serializable node.
    /// </summary>
    public abstract object? OnSerialize(in SerializeContext context, T value);

    /// <summary>
    /// Deserializes a node into a strongly typed value.
    /// </summary>
    public abstract T OnDeserialize(in DeserializeContext context, object? node);
}
