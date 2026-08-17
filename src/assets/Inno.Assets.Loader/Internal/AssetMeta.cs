using System;

using Inno.Core.Serialization;

namespace Inno.Assets.Loader;

internal sealed class AssetMeta : ISerializable
{
    internal const int C_SCHEMA_VERSION = 2;

    [SerializableProperty] internal int schemaVersion { get; set; } = C_SCHEMA_VERSION;
    [SerializableProperty] internal Guid persistentId { get; set; }
    [SerializableProperty] internal string relativePath { get; set; } = string.Empty;
    [SerializableProperty] internal string sourceHash { get; set; } = string.Empty;
    [SerializableProperty] internal string importerId { get; set; } = string.Empty;
    [SerializableProperty] internal int importerVersion { get; set; }
    [SerializableProperty] internal Guid stableAssetTypeId { get; set; }
    [SerializableProperty] internal byte[] assetStateBytes { get; set; } = [];
    [SerializableProperty] internal AssetDependencyData[] runtimeDependencies { get; set; } = [];
    [SerializableProperty] internal AssetImportDependencyData[] importDependencies { get; set; } = [];
}

internal struct AssetDependencyData
{
    [SerializableProperty] internal Guid persistentId { get; set; }
    [SerializableProperty] internal Guid stableTypeId { get; set; }
    [SerializableProperty] internal string lastKnownPath { get; set; }
}

internal struct AssetImportDependencyData
{
    [SerializableProperty] internal int kind { get; set; }
    [SerializableProperty] internal string key { get; set; }
    [SerializableProperty] internal string fingerprint { get; set; }
}
