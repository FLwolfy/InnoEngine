using System;

using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Scene.Layers;

/// <summary>
/// Converts a layer identifier through its stable numeric slot.
/// </summary>
[SerializationExtension]
internal sealed class GameLayerConverter : SerializationConverter<GameLayer>
{
    /// <summary>
    /// Writes the complete value through the configured serialization contract.
    /// </summary>
    /// <param name="writer">
    /// The writer that receives the serialized representation.
    /// </param>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    public override void Write(SerializationWriter writer, GameLayer value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("index", value.index);
    }

    /// <summary>
    /// Reconstructs a complete value through the configured serialization contract.
    /// </summary>
    /// <param name="reader">
    /// The reader that supplies the serialized representation.
    /// </param>
    /// <returns>
    /// The validated game layer that represents the completed operation.
    /// </returns>
    public override GameLayer Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new GameLayer(reader.Read<int>("index"));
    }
}
