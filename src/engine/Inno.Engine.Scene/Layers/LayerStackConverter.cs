using System;
using System.Linq;

using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Engine.Scene.Layers;

/// <summary>
/// Converts a layer stack to and from its fixed-width neutral serialization state.
/// </summary>
[SerializationExtension]
internal sealed class LayerStackConverter : SerializationConverter<LayerStack>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, LayerStack value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.Write(
            "names",
            value.CaptureNames().Select(static name => name ?? string.Empty).ToArray());
        writer.Write("interactionMasks", value.CaptureInteractionMasks());
    }

    /// <inheritdoc />
    public override LayerStack Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string[] serializedNames = reader.Read<string[]>("names");
        string?[] names = serializedNames
            .Select(static name => string.IsNullOrEmpty(name) ? null : name)
            .ToArray();
        return LayerStack.Restore(names, reader.Read<uint[]>("interactionMasks"));
    }
}
