using System;
using System.Linq;
using System.Reflection;

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
    public void AssetObject_DefaultRuntimeState_IsDetachedAndEmpty()
    {
        var asset = new TextAsset();

        Assert.Equal(nameof(TextAsset), asset.name);
        Assert.Equal(string.Empty, asset.sourcePath);
        Assert.False(asset.isMissing);
        Assert.Equal(0, asset.contentVersion);
        Assert.True(asset.runtimePayload.IsEmpty);
    }

    [Fact]
    public void AssetDependency_IsAnImmutableValueContract()
    {
        PropertyInfo[] properties = typeof(AssetDependency).GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.All(properties, static property => Assert.Null(property.SetMethod));
        Assert.True(typeof(AssetDependency).IsValueType);
    }

    [Fact]
    public void AssetObject_PublicSurface_DoesNotExposeSourceHashDependenciesOrSetters()
    {
        string[] propertyNames = typeof(AssetObject)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain("sourceHash", propertyNames);
        Assert.DoesNotContain("dependencies", propertyNames);
        Assert.False(typeof(AssetObject).GetProperty(nameof(AssetObject.sourcePath))!.SetMethod?.IsPublic ?? false);
        Assert.Null(typeof(AssetObject).GetProperty(nameof(AssetObject.contentVersion))!.SetMethod);
        Assert.Null(typeof(AssetObject).GetProperty(nameof(AssetObject.runtimePayload))!.SetMethod);
    }
}
