using System;
using System.Text;

using Inno.Assets.Core;
using Inno.Assets.Types;

using Xunit;

namespace Inno.Assets.Core.Tests;

public sealed class AssetCoreTests
{
    [Fact]
    public void ComputeSha256Hex_IsDeterministic()
    {
        byte[] data = Encoding.UTF8.GetBytes("hello-assets");
        string h1 = AssetHashUtility.ComputeSha256Hex(data);
        string h2 = AssetHashUtility.ComputeSha256Hex(data);
        Assert.Equal(h1, h2);
        Assert.NotEmpty(h1);
    }

    [Fact]
    public void AssetImportContext_ReadUtf8Text_Works()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("abc");
        var ctx = new AssetImportContext("A/B.txt", "/tmp/A/B.txt", bytes, "hash");
        Assert.Equal("abc", ctx.ReadUtf8Text());
        Assert.Equal(".txt", ctx.extension);
    }

    [Fact]
    public void AssetHandle_ToString_ContainsTypeName()
    {
        var handle = new AssetHandle<TextAsset>(Guid.NewGuid(), 7);
        Assert.Contains("TextAsset", handle.ToString());
        Assert.True(handle.isValid);
    }
}
