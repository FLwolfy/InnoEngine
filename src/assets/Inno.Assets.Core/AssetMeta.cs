using System;

using Inno.Core.Serialization;

namespace Inno.Assets.Core;

public sealed class AssetMeta : ISerializable
{
    // Identity
    [SerializableProperty] public Guid persistentId { get; set; }

    // Source
    [SerializableProperty] public string relativePath { get; set; } = string.Empty;
    [SerializableProperty] public string sourceHash { get; set; } = string.Empty;

    // Importer
    [SerializableProperty] public string importerId { get; set; } = string.Empty;
    [SerializableProperty] public int importerVersion { get; set; }

    // Type identity
    [SerializableProperty] public Guid assetTypeStableId { get; set; }
    [SerializableProperty(PropertyVisibility.Transient)] public int assetRuntimeTypeId { get; set; }

    // Asset data
    [SerializableProperty] public byte[] assetStateBytes { get; set; } = [];
    [SerializableProperty] public string[] dependencies { get; set; } = [];
}
