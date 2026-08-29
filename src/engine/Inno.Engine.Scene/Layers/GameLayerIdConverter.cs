using System;

using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Engine.Scene.Layers;

/// <summary>Converts a logical layer identity through its stable string value.</summary>
[SerializationExtension]
internal sealed class GameLayerIdConverter : SerializationConverter<GameLayerId>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, GameLayerId value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("value", value.value);
    }

    /// <inheritdoc />
    public override GameLayerId Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new GameLayerId(reader.Read<string>("value"));
    }
}
