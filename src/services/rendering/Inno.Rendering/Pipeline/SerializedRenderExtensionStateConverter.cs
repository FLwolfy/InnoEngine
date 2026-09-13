using Inno.Assets;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Rendering;

[SerializationExtension]
internal sealed class SerializedRenderExtensionStateConverter : SerializationConverter<SerializedRenderExtensionState>
{
    /// <inheritdoc />
    public override void Write(SerializationWriter writer, SerializedRenderExtensionState value)
    {
        writer.Write("stableTypeId", value.stableTypeId);
        writer.Write("propertyData", value.propertyData ?? []);
        writer.Write("dependencies", value.dependencies ?? []);
        if (writer.context.TryGet(out AssetDependencyCollection? dependencies) && dependencies is not null)
            foreach (AssetDependency dependency in value.dependencies ?? []) dependencies.Add(dependency);
    }

    /// <inheritdoc />
    public override SerializedRenderExtensionState Read(SerializationReader reader) => new()
    {
        stableTypeId = reader.Read<System.Guid>("stableTypeId"),
        propertyData = reader.Read<byte[]>("propertyData"),
        dependencies = reader.Read<AssetDependency[]>("dependencies")
    };
}
