using System;
using System.Collections.Generic;
using Inno.Assets;
using Xunit;

namespace Inno.Assets.Tests;

public sealed class AssetRuntimeOwnerTests
{
    [Fact]
    public void ForeignOwnerCannotMutateOrReleaseCanonicalAsset()
    {
        var first = new AssetRuntimeOwner();
        var foreign = new AssetRuntimeOwner();
        var asset = new TestAsset();
        first.Initialize(asset, AssetPath.Project("one.bin"), "one", new byte[] { 1 }, false, 1);
        Assert.Throws<InvalidOperationException>(() => foreign.Release(asset));
        Assert.Throws<InvalidOperationException>(() => foreign.UpdateAssetPath(asset, AssetPath.Project("two.bin")));
        Assert.Equal(1, asset.runtimePayload.Span[0]);
        first.Release(asset);
        first.Release(asset);
        Assert.Equal(1, asset.unloadCount);
    }

    [Fact]
    public void FailedPayloadHookDoesNotPublishCandidateState()
    {
        var owner = new AssetRuntimeOwner();
        var asset = new TestAsset();
        owner.Initialize(asset, AssetPath.Project("one.bin"), "one", new byte[] { 1 }, false, 1);
        asset.fail = true;
        Assert.Throws<InvalidOperationException>(() => owner.Initialize(asset, AssetPath.Project("two.bin"), "two", new byte[] { 2 }, true, 2));
        Assert.Equal(1, asset.contentVersion);
        Assert.False(asset.isMissing);
        Assert.Equal("one", owner.GetSourceHash(asset));
        Assert.Equal(1, asset.runtimePayload.Span[0]);
        owner.Release(asset);
    }

    [Fact]
    public void ArtifactRetentionCountsIndependentIdempotentLeases()
    {
        var retention = new ArtifactRetention();
        var artifact = new AssetArtifactInfo(new AssetArtifactKey("AABB"), "runtime", "/tmp/value", "HASH", 0);
        using ArtifactLease first = retention.Retain(artifact);
        using ArtifactLease second = retention.Retain(artifact);
        Assert.Single(retention.GetRetainedKeys());
        Assert.Throws<NotSupportedException>(() => ((IList<AssetArtifactKey>)retention.GetRetainedKeys()).Clear());
        first.Dispose();
        first.Dispose();
        Assert.Single(retention.GetRetainedKeys());
        second.Dispose();
        Assert.Empty(retention.GetRetainedKeys());
    }

    private sealed class TestAsset : AssetObject
    {
        internal bool fail;
        internal int unloadCount;
        protected override void OnRuntimePayloadChanged(ReadOnlyMemory<byte> previousPayload, ReadOnlyMemory<byte> currentPayload)
        {
            if (fail) throw new InvalidOperationException("Rejected payload.");
        }
        protected override void OnUnloading() => unloadCount++;
    }
}
