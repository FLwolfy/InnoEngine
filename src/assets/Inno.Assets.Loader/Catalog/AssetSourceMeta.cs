using System;

using Inno.Assets.Core;
using Inno.Core.Serialization;

namespace Inno.Assets.Loader;

internal sealed class AssetSourceMeta : ISerializable
{
    internal const int C_SCHEMA_VERSION = 3;

    [SerializableProperty] internal int schemaVersion { get; set; } = C_SCHEMA_VERSION;
    [SerializableProperty] internal Guid persistentId { get; set; }
    [SerializableProperty] internal int sourceKind { get; set; } = (int)AssetSourceKind.File;
    [SerializableProperty] internal string importerId { get; set; } = string.Empty;
    [SerializableProperty] internal int importerSettingsVersion { get; set; }
    [SerializableProperty] internal byte[] importerSettingsBytes { get; set; } = [];
}
