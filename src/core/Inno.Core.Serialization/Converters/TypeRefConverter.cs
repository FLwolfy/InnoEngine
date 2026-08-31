using System;

using Inno.Core.Reflection;
using Inno.Core.Serialization.Converters;

namespace Inno.Core.Serialization;

[SerializationExtension]
internal sealed class TypeRefConverter : SerializationConverter<TypeRef>
{
    public override void Write(SerializationWriter writer, TypeRef value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("stableId", value.stableId);
    }

    public override TypeRef Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new TypeRef(reader.Read<Guid>("stableId"));
    }
}
