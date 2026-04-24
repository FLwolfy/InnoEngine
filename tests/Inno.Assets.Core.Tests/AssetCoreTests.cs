using System.Text;

using Inno.Assets.Core;
using Inno.Assets.Types;

using Xunit;

namespace Inno.Assets.Core.Tests;

public sealed class AssetCoreTests
{
    [Fact]
    public void AssetImportContext_ReadUtf8Text_Works()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("abc");
        var ctx = new AssetImportContext("A/B.txt", "/tmp/A/B.txt", bytes, "hash");
        Assert.Equal("abc", ctx.ReadUtf8Text());
        Assert.Equal(".txt", ctx.extension);
    }

    [Fact]
    public void AssetRef_ToString_ContainsTypeName()
    {
        AssetRef<TextAsset> handle = default;

        Assert.Contains("TextAsset", handle.ToString());
        Assert.False(handle.isValid);
    }
}
