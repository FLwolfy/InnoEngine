using System;

using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Engine.Scene.Layers;

/// <summary>
/// Converts a layer identifier through its stable numeric slot.
/// </summary>
[SerializationExtension]
internal sealed class LayerConverter : SerializationConverter<Layer>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, Layer value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("index", value.index);
    }

    /// <inheritdoc />
    public override Layer Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new Layer(reader.Read<int>("index"));
    }
}
