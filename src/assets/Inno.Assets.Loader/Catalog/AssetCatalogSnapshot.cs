using Inno.Core.Serialization;

namespace Inno.Assets.Loader;

internal sealed class AssetCatalogSnapshot : ISerializable
{
    internal const int C_SCHEMA_VERSION = 1;

    [SerializableProperty] internal int schemaVersion { get; set; } = C_SCHEMA_VERSION;
    [SerializableProperty] internal long revision { get; set; }
    [SerializableProperty] internal byte[][] entries { get; set; } = [];
}
