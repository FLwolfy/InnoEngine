using System;

using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Engine.Scene.Layers;

/// <summary>
/// Converts a layer mask through its stable thirty-two-bit value.
/// </summary>
[SerializationExtension]
internal sealed class LayerMaskConverter : SerializationConverter<LayerMask>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, LayerMask value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("value", value.value);
    }

    /// <inheritdoc />
    public override LayerMask Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new LayerMask(reader.Read<uint>("value"));
    }
}
