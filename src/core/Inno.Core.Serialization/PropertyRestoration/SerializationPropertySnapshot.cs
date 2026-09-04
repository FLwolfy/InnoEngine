using System;

namespace Inno.Core.Serialization;

/// <summary>
/// Stores one independently encoded serializable property and its original declared type.
/// </summary>
public sealed class SerializationPropertySnapshot
{
    private readonly byte[] m_data;

    internal SerializationPropertySnapshot(string name, Type propertyType, byte[] data)
    {
        this.name = name;
        this.propertyType = propertyType;
        m_data = data;
    }

    /// <summary>
    /// Gets the serialized member key.
    /// </summary>
    public string name { get; }

    /// <summary>
    /// Gets the declared property type used when the value was captured.
    /// </summary>
    public Type propertyType { get; }

    /// <summary>
    /// Gets the independently encoded property data.
    /// </summary>
    public ReadOnlyMemory<byte> data => m_data;

    internal ReadOnlySpan<byte> dataSpan => m_data;
}
