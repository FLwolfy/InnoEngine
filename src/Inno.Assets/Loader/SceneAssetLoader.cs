using System;
using System.Text;
using Inno.Assets.AssetType;
using Inno.Assets.Core;
using Inno.Assets.Serializer;
using Inno.Core.ECS;
using Inno.Core.Serialization;

namespace Inno.Assets.Loader;

/// <summary>
/// The raw source bytes and runtime binaries are identical (canonicalized by re-encoding).
/// </summary>
internal sealed class SceneAssetLoader : InnoAssetLoader<SceneAsset>
{
    public override string[] validExtensions => [".scene"];

    protected override byte[] OnLoadBinaries(string assetName, byte[] rawBytes, out SceneAsset asset)
    {
        var yaml = Encoding.UTF8.GetString(rawBytes);
        var sceneState = AssetYamlSerializer.DeserializeStateFromYaml(yaml);

        asset = new SceneAsset(sceneState);
        var bytes = SerializingState.Serialize(sceneState);

        return bytes;
    }

    protected override byte[] OnSaveSource(string assetName, in SceneAsset asset)
    {
        var sceneState = asset.sceneState;
        var yaml = AssetYamlSerializer.SerializeStateToYaml(sceneState);
        
        return Encoding.UTF8.GetBytes(yaml);
    }
}