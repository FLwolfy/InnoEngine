using System;

using Inno.Assets.Core;
using Inno.Core.Serialization;

namespace Inno.Assets.Loader;

internal sealed class AssetMeta : ISerializable
{
    internal const int C_SCHEMA_VERSION = 3;

    [SerializableProperty] internal int schemaVersion { get; set; } = C_SCHEMA_VERSION;
    [SerializableProperty] internal Guid persistentId { get; set; }
    [SerializableProperty] internal string relativePath { get; set; } = string.Empty;
    [SerializableProperty] internal string sourceHash { get; set; } = string.Empty;
    [SerializableProperty] internal long sourceLength { get; set; } = -1;
    [SerializableProperty] internal long sourceLastWriteUtcTicks { get; set; }
    [SerializableProperty] internal long sourceCreationTimeUtcTicks { get; set; }
    [SerializableProperty] internal string importerId { get; set; } = string.Empty;
    [SerializableProperty] internal int importerVersion { get; set; }
    [SerializableProperty] internal Guid stableAssetTypeId { get; set; }
    [SerializableProperty] internal byte[] assetStateBytes { get; set; } = [];
    [SerializableProperty] internal AssetDependencyData[] runtimeDependencies { get; set; } = [];
    [SerializableProperty] internal AssetImportDependencyData[] importDependencies { get; set; } = [];
    [SerializableProperty] internal int importStatus { get; set; } = (int)AssetImportStatus.Pending;
    [SerializableProperty] internal string importerImplementationFingerprint { get; set; } = string.Empty;
    [SerializableProperty] internal string artifactKey { get; set; } = string.Empty;
    [SerializableProperty] internal string lastSuccessfulArtifactKey { get; set; } = string.Empty;
    [SerializableProperty] internal string[] diagnostics { get; set; } = [];
    [SerializableProperty] internal bool isDirectory { get; set; }
    [SerializableProperty] internal bool isTombstone { get; set; }
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
    [SerializableProperty] internal bool sourceStampValid { get; set; }
    [SerializableProperty] internal long sourceLength { get; set; }
    [SerializableProperty] internal long sourceLastWriteUtcTicks { get; set; }
    [SerializableProperty] internal long sourceCreationTimeUtcTicks { get; set; }
}
