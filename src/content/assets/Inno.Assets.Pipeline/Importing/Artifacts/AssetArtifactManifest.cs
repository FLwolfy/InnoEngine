using Inno.Core.Serialization;

namespace Inno.Assets.Pipeline;

internal sealed class AssetArtifactManifest : ISerializable
{
    [SerializableProperty] internal string key { get; set; } = string.Empty;
    [SerializableProperty] internal AssetArtifactOutputData[] outputs { get; set; } = [];
}

internal struct AssetArtifactOutputData
{
    [SerializableProperty] internal string name { get; set; }
    [SerializableProperty] internal string fileName { get; set; }
    [SerializableProperty] internal string contentHash { get; set; }
    [SerializableProperty] internal long length { get; set; }
}
