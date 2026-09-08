using System;

using Inno.Assets;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

namespace Inno.Assets;

[SerializationExtension]
internal sealed class AssetObjectConverter : SerializationConverter<AssetObject>
{
    private const string C_PERSISTENT_ID = "persistentId";
    private const string C_STABLE_TYPE_ID = "stableTypeId";
    private const string C_LAST_KNOWN_PATH = "lastKnownPath";

    /// <summary>
    /// Writes the supplied value through the owning subsystem's validated output boundary.
    /// </summary>
    /// <param name="writer">
    /// The writer that receives the deterministic structured representation.
    /// </param>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    public override void Write(SerializationWriter writer, AssetObject value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        Guid persistentId = value.identity.persistentId;
        if (persistentId == Guid.Empty)
            throw new InvalidOperationException($"Asset '{value.GetType().FullName}' has no persistent identity at '{writer.path}'.");
        TypeCatalog types = writer.context.GetRequired<TypeCatalog>();
        if (!types.TryGetTypeRef(value.GetType(), out TypeRef typeRef))
        {
            throw new InvalidOperationException(
                $"Asset type '{value.GetType().FullName}' requires a StableTypeId at '{writer.path}'.");
        }

        bool hasDependencies = writer.context.TryGet(out AssetDependencyCollection? dependencies) &&
                               dependencies is not null;
        writer.Write(C_PERSISTENT_ID, persistentId);
        writer.Write(C_STABLE_TYPE_ID, typeRef.stableId);
        writer.Write(
            C_LAST_KNOWN_PATH,
            !hasDependencies || dependencies!.includeLastKnownPaths ? value.assetPath.ToString() : string.Empty);
        if (hasDependencies)
            dependencies!.Add(value, types);
    }

    /// <summary>
    /// Reads and validates the requested value without transferring storage ownership.
    /// </summary>
    /// <param name="reader">
    /// The reader positioned at the structured value to decode.
    /// </param>
    /// <returns>
    /// The validated asset object that represents the completed operation.
    /// </returns>
    public override AssetObject Read(SerializationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader.context.GetRequired<IAssetReferenceResolver>().Resolve(
            reader.Read<Guid>(C_PERSISTENT_ID),
            reader.Read<Guid>(C_STABLE_TYPE_ID),
            reader.Read<string>(C_LAST_KNOWN_PATH),
            reader.valueType,
            reader.path);
    }
}
