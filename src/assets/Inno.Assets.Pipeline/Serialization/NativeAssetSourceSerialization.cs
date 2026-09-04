using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Imports and exports editable asset source state through the common native serializer.
/// </summary>
public static class NativeAssetSourceSerialization
{
    /// <summary>
    /// Serializes one asset's editable properties and direct asset dependencies.
    /// </summary>
    /// <typeparam name="TAsset">
    /// Concrete asset source type.
    /// </typeparam>
    /// <param name="asset">
    /// Unsaved or loaded asset whose editable state should be captured.
    /// </param>
    /// <param name="services">
    /// The generation-bound serialization services supplied by the export context.
    /// </param>
    /// <returns>
    /// Deterministic native source bytes.
    /// </returns>
    public static byte[] Export<TAsset>(
        TAsset asset,
        AssetSerializationServices services)
        where TAsset : AssetObject
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(services);
        TypeRef type = services.types.GetTypeRef(asset.GetType());
        var dependencies = new AssetDependencyCollection();
        SerializationContext context = SerializationContext.empty.With(dependencies);
        byte[] propertyData = services.serialization.Encode(
            writer => writer.WriteProperties(asset),
            context);
        return services.serialization.Serialize(new NativeAssetSourceDocument
        {
            stableAssetTypeId = type.stableId,
            propertyData = propertyData,
            dependencies = dependencies.dependencies.ToArray()
        });
    }

    /// <summary>
    /// Restores one concrete asset and its declared direct dependencies.
    /// </summary>
    /// <typeparam name="TAsset">
    /// Concrete asset source type.
    /// </typeparam>
    /// <param name="bytes">
    /// Native source bytes.
    /// </param>
    /// <param name="dependencies">
    /// Receives persistent dependencies discovered during export.
    /// </param>
    /// <param name="services">
    /// The generation-bound serialization and reference services supplied by the import context.
    /// </param>
    /// <returns>
    /// A detached asset ready for an import transaction.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the source type identity is incompatible.
    /// </exception>
    public static TAsset Import<TAsset>(
        ReadOnlySpan<byte> bytes,
        AssetSerializationServices services,
        out IReadOnlyList<AssetDependency> dependencies)
        where TAsset : AssetObject
    {
        ArgumentNullException.ThrowIfNull(services);
        NativeAssetSourceDocument document = services.serialization.Deserialize<NativeAssetSourceDocument>(bytes);
        TypeRef expectedType = services.types.GetTypeRef(typeof(TAsset));
        if (document.stableAssetTypeId != expectedType.stableId)
        {
            throw new InvalidOperationException(
                $"Native asset source type '{document.stableAssetTypeId:D}' does not match " +
                $"'{expectedType.stableId:D}'.");
        }
        var asset = (TAsset)(Activator.CreateInstance(typeof(TAsset), nonPublic: true)
            ?? throw new InvalidOperationException($"Asset type '{typeof(TAsset).FullName}' could not be created."));
        SerializationContext context = SerializationContext.empty.With(services.references);
        _ = services.serialization.Decode(document.propertyData, reader =>
        {
            reader.RestoreProperties(asset);
            return 0;
        }, context);
        dependencies = document.dependencies?.ToArray() ?? [];
        return asset;
    }

    private sealed class NativeAssetSourceDocument : ISerializable
    {
        /// <summary>
        /// Gets the stable type identity stored with serialized asset data.
        /// </summary>
        [SerializableProperty]
        public Guid stableAssetTypeId { get; set; }

        /// <summary>
        /// Gets the neutral serialized property payload owned by this record.
        /// </summary>
        [SerializableProperty]
        public byte[] propertyData { get; set; } = [];

        /// <summary>
        /// Gets the stable dependency identities required by this value.
        /// </summary>
        [SerializableProperty]
        public AssetDependency[] dependencies { get; set; } = [];
    }
}
