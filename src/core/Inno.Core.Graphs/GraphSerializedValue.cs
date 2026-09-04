using System;
using Inno.Core.Serialization;
using Inno.Scripting.Api;

namespace Inno.Core.Graphs;

/// <summary>
/// Stores one graph property as backend-neutral Inno serialization bytes.
/// </summary>
public sealed class GraphSerializedValue
{
    private readonly byte[] m_data;

    /// <summary>
    /// Creates a graph value from one complete native serialization payload.
    /// </summary>
    /// <param name="data">
    /// Bytes produced by the common Inno serialization pipeline.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the payload is empty.
    /// </exception>
    public GraphSerializedValue(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            throw new ArgumentException("A graph value payload cannot be empty.", nameof(data));
        m_data = data.ToArray();
    }

    /// <summary>
    /// Gets an immutable view of the native serialized value.
    /// </summary>
    public ReadOnlyMemory<byte> data => m_data;

    /// <summary>
    /// Serializes a neutral graph property through the common Inno serializer.
    /// </summary>
    /// <typeparam name="T">
    /// Declared serializable value type.
    /// </typeparam>
    /// <param name="value">
    /// Value to serialize.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the converter generation for this operation.
    /// </param>
    /// <returns>
    /// A detached graph value containing native serialization bytes.
    /// </returns>
    [ScriptingApiIgnore]
    public static GraphSerializedValue From<T>(T value, SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        return new GraphSerializedValue(serialization.Encode(writer => writer.Write("value", value)));
    }

    /// <summary>
    /// Deserializes this value through the common Inno serializer.
    /// </summary>
    /// <typeparam name="T">
    /// Requested declared result type.
    /// </typeparam>
    /// <param name="serialization">
    /// The serialization registry that owns the converter generation for this operation.
    /// </param>
    /// <returns>
    /// The restored neutral value.
    /// </returns>
    [ScriptingApiIgnore]
    public T Deserialize<T>(SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        return serialization.Decode(m_data, reader => reader.Read<T>("value"));
    }

    /// <summary>
    /// Creates an independent copy of the neutral value.
    /// </summary>
    /// <returns>
    /// A graph value that shares no mutable byte storage.
    /// </returns>
    public GraphSerializedValue Clone() => new(m_data);

    /// <summary>
    /// Copies the native payload for persistence or reload-safe history.
    /// </summary>
    /// <returns>
    /// A new byte array containing the complete native value.
    /// </returns>
    public byte[] ToArray() => (byte[])m_data.Clone();
}
