using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets;
using Xunit;

namespace Inno.Assets.Tests;

public sealed class AssetResidencyTests
{
    [Fact]
    public async Task AssetLeaseRetainsCanonicalAssetAndReleasesExactlyOnce()
    {
        var provider = new FakeResidencyProvider();
        AssetLease<TextAsset> lease = await provider.AcquireAsync<TextAsset>(
            AssetPath.Project("Text/readme.txt"));

        Assert.Same(provider.asset, lease.asset);
        lease.Dispose();
        lease.Dispose();

        Assert.Equal(1, provider.assetReleaseCount);
        Assert.Throws<ObjectDisposedException>(() => lease.asset);
    }

    [Fact]
    public void RetentionScopeReleasesChildrenInReverseOrder()
    {
        var order = new List<int>();
        using (var scope = new RetentionScope())
        {
            _ = scope.Retain(new CallbackLease(() => order.Add(1)));
            _ = scope.Retain(new CallbackLease(() => order.Add(2)));
        }

        Assert.Equal([2, 1], order);
    }

    [Fact]
    public void ArtifactLeaseOpensVerifiedPathAndInvalidatesAfterRelease()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            var provider = new FakeResidencyProvider(path);
            ArtifactLease lease = provider.AcquireArtifact(Guid.NewGuid(), "runtime");
            using (Stream stream = lease.OpenRead())
                Assert.Equal(3, stream.Length);

            lease.Dispose();

            Assert.Equal(1, provider.artifactReleaseCount);
            Assert.Throws<ObjectDisposedException>(() => lease.info);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class FakeResidencyProvider : AssetResidencyProvider, IAssetResidency
    {
        private readonly string m_artifactPath;

        internal FakeResidencyProvider(string artifactPath = "unused")
        {
            m_artifactPath = artifactPath;
        }

        internal TextAsset asset { get; } = new();

        internal int assetReleaseCount { get; private set; }

        internal int artifactReleaseCount { get; private set; }

        public ValueTask<AssetLease<TAsset>> AcquireAsync<TAsset>(
            AssetPath path,
            CancellationToken cancellationToken = default)
            where TAsset : AssetObject
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CreateAssetLease(
                (TAsset)(AssetObject)asset,
                () => assetReleaseCount++));
        }

        public ValueTask<AssetLease<TAsset>> AcquireAsync<TAsset>(
            Guid persistentId,
            CancellationToken cancellationToken = default)
            where TAsset : AssetObject
            => AcquireAsync<TAsset>(AssetPath.Project("Text/readme.txt"), cancellationToken);

        public ArtifactLease AcquireArtifact(Guid persistentId, string outputName)
            => CreateArtifactLease(
                new AssetArtifactInfo(
                    new AssetArtifactKey("AA"),
                    outputName,
                    m_artifactPath,
                    "HASH",
                    3),
                () => artifactReleaseCount++);
    }

    private sealed class CallbackLease(Action callback) : IDisposable
    {
        private Action? m_callback = callback;

        public void Dispose()
        {
            Action? current = m_callback;
            m_callback = null;
            current?.Invoke();
        }
    }
}
