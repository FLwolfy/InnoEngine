using System;

using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Scene.Layers;

/// <summary>
/// Converts a layer mask through its stable thirty-two-bit value.
/// </summary>
[SerializationExtension]
internal sealed class GameLayerMaskConverter : SerializationConverter<GameLayerMask>
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
    public override void Write(SerializationWriter writer, GameLayerMask value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("value", value.value);
    }

    /// <summary>
    /// Reconstructs a complete value through the configured serialization contract.
    /// </summary>
    /// <param name="reader">
    /// The reader that supplies the serialized representation.
    /// </param>
    /// <returns>
    /// The validated game layer mask that represents the completed operation.
    /// </returns>
    public override GameLayerMask Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new GameLayerMask(reader.Read<uint>("value"));
    }
}
