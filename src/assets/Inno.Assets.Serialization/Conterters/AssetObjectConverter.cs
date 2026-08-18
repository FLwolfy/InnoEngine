using System;

using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Assets.Serialization;

[SerializationExtension]
internal sealed class AssetObjectConverter : SerializationConverter<AssetObject>
{
    private const string C_PERSISTENT_ID = "persistentId";
    private const string C_STABLE_TYPE_ID = "stableTypeId";
    private const string C_LAST_KNOWN_PATH = "lastKnownPath";

    public override void Write(SerializationWriter writer, AssetObject value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        Guid persistentId = value.identity.persistentId;
        if (persistentId == Guid.Empty)
            throw new InvalidOperationException($"Asset '{value.GetType().FullName}' has no persistent identity at '{writer.path}'.");
        if (!TypeCacheManager.TryGetStableTypeId(value.GetType(), out Guid stableTypeId))
        {
            throw new InvalidOperationException(
                $"Asset type '{value.GetType().FullName}' requires a StableTypeId at '{writer.path}'.");
        }

        writer.Write(C_PERSISTENT_ID, persistentId);
        writer.Write(C_STABLE_TYPE_ID, stableTypeId);
        writer.Write(C_LAST_KNOWN_PATH, value.sourcePath);
        if (writer.context.TryGet(out AssetDependencyCollection? dependencies) && dependencies is not null)
            dependencies.Add(value);
    }

    public override AssetObject Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return AssetSerializationServices.ResolveReference(
            reader.Read<Guid>(C_PERSISTENT_ID),
            reader.Read<Guid>(C_STABLE_TYPE_ID),
            reader.Read<string>(C_LAST_KNOWN_PATH),
            reader.valueType,
            reader.path);
    }
}
