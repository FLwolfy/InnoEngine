using Inno.Assets;

using Xunit;

namespace Inno.Assets.Tests;

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
    public void BinaryAsset_Ctor_AssignsValues()
    {
        var asset = new BinaryAsset(10);
        Assert.Equal(10, asset.byteLength);
    }
}
