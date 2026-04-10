using System;

namespace Inno.Core.Serialization;

/// <summary>
/// Serialization context passed to custom codecs.
/// </summary>
public readonly struct SerializeContext
{
    private readonly Func<object?, Type, object?> m_serialize;

    internal SerializeContext(Func<object?, Type, object?> serialize)
    {
        m_serialize = serialize;
    }

    /// <summary>
    /// Serializes a value using the default pipeline for the specified type.
    /// </summary>
    public object? Serialize(object? value, Type declaredType)
        => m_serialize(value, declaredType);

    /// <summary>
    /// Serializes a value using the default pipeline for <typeparamref name="T"/>.
    /// </summary>
    public object? Serialize<T>(T value)
        => m_serialize(value, typeof(T));
}

/// <summary>
/// Deserialization context passed to custom codecs.
/// </summary>
public readonly struct DeserializeContext
{
    private readonly Func<object?, Type, object?> m_deserialize;

    internal DeserializeContext(Func<object?, Type, object?> deserialize)
    {
        m_deserialize = deserialize;
    }

    /// <summary>
    /// Deserializes a node using the default pipeline for the specified type.
    /// </summary>
    public object? Deserialize(object? node, Type declaredType)
        => m_deserialize(node, declaredType);

    /// <summary>
    /// Deserializes a node using the default pipeline for <typeparamref name="T"/>.
    /// </summary>
    public T Deserialize<T>(object? node)
        => (T)m_deserialize(node, typeof(T))!;
}
