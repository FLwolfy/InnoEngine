using Inno.Assets.Types;

using Xunit;

namespace Inno.Assets.Types.Tests;

public sealed class AssetTypesTests
{
    [Fact]
    public void TextAsset_Ctor_AssignsValues()
    {
        var asset = new TextAsset("hello", "plain");
        Assert.Equal("hello", asset.content);
        Assert.Equal("plain", asset.languageHint);
    }

    [Fact]
    public void TextureAsset_Ctor_AssignsValues()
    {
        var asset = new TextureAsset(10, 20, 4, "png");
        Assert.Equal(10, asset.width);
        Assert.Equal(20, asset.height);
        Assert.Equal("png", asset.encoding);
    }
}
