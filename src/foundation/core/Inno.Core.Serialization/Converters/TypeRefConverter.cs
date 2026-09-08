using System;

using Inno.Extensibility.Types;
using Inno.Core.Serialization.Converters;

namespace Inno.Core.Serialization;

[SerializationExtension]
internal sealed class TypeRefConverter : SerializationConverter<TypeRef>
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
    public override void Write(SerializationWriter writer, TypeRef value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("stableId", value.stableId);
    }

    /// <summary>
    /// Reads and validates the requested value without transferring storage ownership.
    /// </summary>
    /// <param name="reader">
    /// The reader positioned at the structured value to decode.
    /// </param>
    /// <returns>
    /// The validated type ref that represents the completed operation.
    /// </returns>
    public override TypeRef Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new TypeRef(reader.Read<Guid>("stableId"));
    }
}
