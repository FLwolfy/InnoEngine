using Inno.Assets.Core;
using Inno.Assets.Types;

using Xunit;

namespace Inno.Assets.Core.Tests;

public sealed class AssetCoreTests
{
    [Fact]
    public void AssetRef_ToString_ContainsTypeName()
    {
        AssetRef<TextAsset> handle = default;

        Assert.Contains("TextAsset", handle.ToString());
        Assert.False(handle.isValid);
    }
}
