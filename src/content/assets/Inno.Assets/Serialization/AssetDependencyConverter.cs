using System;

using Inno.Assets;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Assets;

[SerializationExtension]
internal sealed class AssetDependencyConverter : SerializationConverter<AssetDependency>
{
    /// <summary>
    /// Writes the supplied value through the owning subsystem's validated output boundary.
    /// </summary>
    /// <param name="writer">
    /// The writer that receives the deterministic structured representation.
    /// </param>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    public override void Write(SerializationWriter writer, AssetDependency value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("persistentId", value.persistentId);
        writer.Write("stableTypeId", value.type.stableId);
        writer.Write("lastKnownPath", value.lastKnownPath);
    }

    /// <summary>
    /// Reads and validates the requested value without transferring storage ownership.
    /// </summary>
    /// <param name="reader">
    /// The reader positioned at the structured value to decode.
    /// </param>
    /// <returns>
    /// The validated asset dependency that represents the completed operation.
    /// </returns>
    public override AssetDependency Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new AssetDependency(
            reader.Read<Guid>("persistentId"),
            new TypeRef(reader.Read<Guid>("stableTypeId")),
            reader.Read<string>("lastKnownPath"));
    }
}
