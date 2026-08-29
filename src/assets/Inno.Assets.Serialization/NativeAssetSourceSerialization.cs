using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Assets.Serialization;

/// <summary>Imports and exports editable asset source state through the common native serializer.</summary>
public static class NativeAssetSourceSerialization
{
    /// <summary>Serializes one asset's editable properties and direct asset dependencies.</summary>
    /// <typeparam name="TAsset">Concrete asset source type.</typeparam>
    /// <param name="asset">Unsaved or loaded asset whose editable state should be captured.</param>
    /// <returns>Deterministic native source bytes.</returns>
    public static byte[] Export<TAsset>(TAsset asset) where TAsset : AssetObject
    {
        ArgumentNullException.ThrowIfNull(asset);
        TypeRef type = TypeCacheManager.GetTypeRef(asset.GetType());
        var dependencies = new AssetDependencyCollection();
        SerializationContext context = SerializationContext.empty.With(dependencies);
        byte[] propertyData = SerializationManager.Encode(
            writer => writer.WriteProperties(asset),
            context);
        return SerializationManager.Serialize(new NativeAssetSourceDocument
        {
            stableAssetTypeId = type.stableId,
            propertyData = propertyData,
            dependencies = dependencies.dependencies.ToArray()
        });
    }

    /// <summary>Restores one concrete asset and its declared direct dependencies.</summary>
    /// <typeparam name="TAsset">Concrete asset source type.</typeparam>
    /// <param name="bytes">Native source bytes.</param>
    /// <param name="dependencies">Receives persistent dependencies discovered during export.</param>
    /// <returns>A detached asset ready for an import transaction.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the source type identity is incompatible.</exception>
    public static TAsset Import<TAsset>(
        ReadOnlySpan<byte> bytes,
        out IReadOnlyList<AssetDependency> dependencies)
        where TAsset : AssetObject
    {
        NativeAssetSourceDocument document = SerializationManager.Deserialize<NativeAssetSourceDocument>(bytes);
        TypeRef expectedType = TypeCacheManager.GetTypeRef(typeof(TAsset));
        if (document.stableAssetTypeId != expectedType.stableId)
        {
            throw new InvalidOperationException(
                $"Native asset source type '{document.stableAssetTypeId:D}' does not match " +
                $"'{expectedType.stableId:D}'.");
        }
        var asset = (TAsset)(Activator.CreateInstance(typeof(TAsset), nonPublic: true)
            ?? throw new InvalidOperationException($"Asset type '{typeof(TAsset).FullName}' could not be created."));
        _ = SerializationManager.Decode(document.propertyData, reader =>
        {
            reader.RestoreProperties(asset);
            return 0;
        });
        dependencies = document.dependencies?.ToArray() ?? [];
        return asset;
    }

    private sealed class NativeAssetSourceDocument : ISerializable
    {
        [SerializableProperty]
        public Guid stableAssetTypeId { get; set; }

        [SerializableProperty]
        public byte[] propertyData { get; set; } = [];

        [SerializableProperty]
        public AssetDependency[] dependencies { get; set; } = [];
    }
}
