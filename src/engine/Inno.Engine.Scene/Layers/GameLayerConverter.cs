using System;

using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Engine.Scene.Layers;

/// <summary>
/// Converts a layer identifier through its stable numeric slot.
/// </summary>
[SerializationExtension]
internal sealed class GameLayerConverter : SerializationConverter<GameLayer>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, GameLayer value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("index", value.index);
    }

    /// <inheritdoc />
    public override GameLayer Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new GameLayer(reader.Read<int>("index"));
    }
}
