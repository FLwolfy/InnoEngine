using Inno.Assets.Core;
using Inno.Core.Serialization;

namespace Inno.Assets.Types;

public sealed class TextureAsset : AssetObject
{
    [SerializableProperty]
    public int width { get; private set; }

    [SerializableProperty]
    public int height { get; private set; }

    [SerializableProperty]
    public int channelCount { get; private set; } = 4;

    [SerializableProperty]
    public string encoding { get; private set; } = "png";

    public TextureAsset()
    {
    }

    public TextureAsset(int width, int height, int channelCount = 4, string encoding = "png")
    {
        this.width = width;
        this.height = height;
        this.channelCount = channelCount;
        this.encoding = encoding ?? "png";
    }
}
