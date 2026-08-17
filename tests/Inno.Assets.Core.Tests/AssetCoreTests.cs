using System;

using Inno.Assets.Core;
using Inno.Assets.Types;

using Xunit;

namespace Inno.Assets.Core.Tests;

public sealed class AssetCoreTests
{
    [Fact]
    public void AssetDependency_UsesPersistentIdentityForEquality()
    {
        Guid persistentId = Guid.NewGuid();
        var first = new AssetDependency(persistentId, Guid.NewGuid(), "A/first.txt");
        var second = new AssetDependency(persistentId, Guid.NewGuid(), "B/second.txt");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void AssetObject_DefaultRuntimeState_IsNotMissingAndHasNoDependencies()
    {
        var asset = new TextAsset();

        Assert.False(asset.isMissing);
        Assert.Empty(asset.dependencies);
        Assert.True(asset.runtimePayload.IsEmpty);
    }
}
