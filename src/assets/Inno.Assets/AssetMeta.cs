using System;

using Inno.Core.Serialization;

namespace Inno.Assets;

internal sealed class AssetMeta : ISerializable
{
    [SerializableProperty] public Guid persistentId { get; set; }
    [SerializableProperty] public string relativePath { get; set; } = string.Empty;
    [SerializableProperty] public string sourceHash { get; set; } = string.Empty;
    [SerializableProperty] public string importerId { get; set; } = string.Empty;
    [SerializableProperty] public int importerVersion { get; set; }
    [SerializableProperty] public Guid assetTypeStableId { get; set; }
    [SerializableProperty] public int assetRuntimeTypeId { get; set; }
    [SerializableProperty] public byte[] assetStateBytes { get; set; } = [];
    [SerializableProperty] public string[] dependencies { get; set; } = [];
}
