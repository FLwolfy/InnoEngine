using System;

using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Assets.Serialization;

[SerializationExtension]
internal sealed class AssetDependencyConverter : SerializationConverter<AssetDependency>
{
    public override void Write(SerializationWriter writer, AssetDependency value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("persistentId", value.persistentId);
        writer.Write("stableTypeId", value.type.stableId);
        writer.Write("lastKnownPath", value.lastKnownPath);
    }

    public override AssetDependency Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new AssetDependency(
            reader.Read<Guid>("persistentId"),
            new TypeRef(reader.Read<Guid>("stableTypeId")),
            reader.Read<string>("lastKnownPath"));
    }
}
