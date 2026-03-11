using Inno.Core.Serialization;

namespace Inno.Assets.AssetType;

/// <summary>
/// Scene asset.
/// </summary>
public sealed class SceneAsset : InnoAsset
{
    /// <summary>
    /// This scene state 
    /// </summary>
    internal readonly SerializingState sceneState;

    public SceneAsset(SerializingState sceneState)
    {
        this.sceneState = sceneState;
    }
}