using System;
using System.Linq;

using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Scene.Layers;

/// <summary>
/// Converts a layer stack to and from its fixed-width neutral serialization state.
/// </summary>
[SerializationExtension]
internal sealed class GameLayerStackConverter : SerializationConverter<GameLayerStack>
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
    public override void Write(SerializationWriter writer, GameLayerStack value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.Write(
            "ids",
            value.CaptureIds().Select(static id => id ?? string.Empty).ToArray());
        writer.Write(
            "names",
            value.CaptureNames().Select(static name => name ?? string.Empty).ToArray());
        writer.Write("interactionMasks", value.CaptureInteractionMasks());
    }

    /// <summary>
    /// Reconstructs a complete value through the configured serialization contract.
    /// </summary>
    /// <param name="reader">
    /// The reader that supplies the serialized representation.
    /// </param>
    /// <returns>
    /// The validated game layer stack that represents the completed operation.
    /// </returns>
    public override GameLayerStack Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string[] serializedIds = reader.Read<string[]>("ids");
        string?[] ids = serializedIds
            .Select(static id => string.IsNullOrEmpty(id) ? null : id)
            .ToArray();
        string[] serializedNames = reader.Read<string[]>("names");
        string?[] names = serializedNames
            .Select(static name => string.IsNullOrEmpty(name) ? null : name)
            .ToArray();
        return GameLayerStack.Restore(ids, names, reader.Read<uint[]>("interactionMasks"));
    }
}
