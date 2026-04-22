using Inno.Assets.Core;
using Inno.Core.Serialization;

namespace Inno.Assets.Types;

/// <summary>
/// Scene asset.
/// </summary>
public sealed class SceneAsset : InnoAsset
{
    /// <summary>
    /// This scene state 
    /// </summary>
    public readonly SerializingState sceneState;

    public SceneAsset(SerializingState sceneState)
    {
        this.sceneState = sceneState;
    }
}