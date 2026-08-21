using System;

using Inno.Assets.Core;
using Inno.Core.Serialization;

namespace Inno.Assets.Loader;

internal sealed class AssetSourceMeta : ISerializable
{
    [SerializableProperty] internal Guid persistentId { get; set; }
    [SerializableProperty] internal int sourceKind { get; set; } = (int)AssetSourceKind.File;
    [SerializableProperty] internal string importerId { get; set; } = string.Empty;
    [SerializableProperty] internal byte[] importerSettingsBytes { get; set; } = [];
}
