using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Assets.Types;

/// <summary>
/// Describes an imported texture payload.
/// </summary>
[StableTypeId("a4e2f6f1-378c-487f-adf2-59e260ac82d4")]
public sealed class TextureAsset : AssetObject
{
    /// <summary>
    /// Gets the texture width in pixels.
    /// </summary>
    [SerializableProperty]
    public int width { get; private set; }

    /// <summary>
    /// Gets the texture height in pixels.
    /// </summary>
    [SerializableProperty]
    public int height { get; private set; }

    /// <summary>
    /// Gets the number of color channels.
    /// </summary>
    [SerializableProperty]
    public int channelCount { get; private set; } = 4;

    /// <summary>
    /// Gets the source encoding name.
    /// </summary>
    [SerializableProperty]
    public string encoding { get; private set; } = "png";

    /// <summary>
    /// Creates an empty texture asset descriptor.
    /// </summary>
    public TextureAsset()
    {
    }

    /// <summary>
    /// Creates a texture asset descriptor.
    /// </summary>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <param name="channelCount">Number of color channels.</param>
    /// <param name="encoding">Source encoding name.</param>
    public TextureAsset(int width, int height, int channelCount = 4, string encoding = "png")
    {
        this.width = width;
        this.height = height;
        this.channelCount = channelCount;
        this.encoding = encoding ?? "png";
    }
}
