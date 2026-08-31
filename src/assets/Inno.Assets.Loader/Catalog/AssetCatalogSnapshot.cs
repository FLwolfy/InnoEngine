using Inno.Core.Serialization;

namespace Inno.Assets.Loader;

internal sealed class AssetCatalogSnapshot : ISerializable
{
    [SerializableProperty] internal long revision { get; set; }
    [SerializableProperty] internal byte[][] entries { get; set; } = [];
}
