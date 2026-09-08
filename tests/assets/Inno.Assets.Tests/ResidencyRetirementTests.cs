using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Inno.Core.Execution;
using Xunit;

namespace Inno.Assets.Tests;

public sealed class ResidencyRetirementTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PendingLeaseRetainsItsValueAndCallbackUntilReleaseCompletes(bool artifact)
    {
        int attempts = 0;
        var provider = new TestProvider();
        var asset = new TextAsset();
        AssetArtifactInfo info = Artifact();
        Action release = () =>
        {
            if (++attempts == 1)
                throw new RetirementPendingException("Expected residency drain.");
        };
        using IDisposable lease = artifact ? provider.Artifact(info, release) : provider.Asset(asset, release);
        Assert.Throws<RetirementPendingException>(lease.Dispose);
        if (lease is ArtifactLease artifactLease) Assert.Same(info, artifactLease.info);
        else Assert.Same(asset, ((AssetLease<TextAsset>)lease).asset);
        lease.Dispose();
        Assert.Equal(2, attempts);
        lease.Dispose();
        Assert.Equal(2, attempts);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            if (lease is ArtifactLease releasedArtifact) _ = releasedArtifact.info;
            else _ = ((AssetLease<TextAsset>)lease).asset;
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrdinaryReleaseFailureIsTerminalAndNeverInvokesTheCallbackTwice(bool artifact)
    {
        int attempts = 0;
        var provider = new TestProvider();
        Action release = () => { attempts++; throw new InvalidOperationException("Expected terminal release failure."); };
        IDisposable lease = artifact ? provider.Artifact(Artifact(), release) : provider.Asset(new TextAsset(), release);
        Assert.Throws<InvalidOperationException>(lease.Dispose);
        lease.Dispose();
        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WrappedPendingRetainsTheLeaseAndReportsOrdinarySiblingsAfterCompletion(bool artifact)
    {
        int attempts = 0;
        var provider = new TestProvider();
        var asset = new TextAsset();
        var ordinary = new InvalidOperationException("completed sibling");
        var pending = new AggregateException(ordinary, new RetirementPendingException("unfinished callback"));
        Action release = () => { if (++attempts < 3) throw pending; };
        IDisposable lease = artifact ? provider.Artifact(Artifact(), release) : provider.Asset(asset, release);
        Assert.Same(pending, Assert.Throws<AggregateException>(lease.Dispose));
        Assert.Same(pending, Assert.Throws<AggregateException>(lease.Dispose));
        if (lease is ArtifactLease artifactLease) Assert.NotNull(artifactLease.info);
        else Assert.Same(asset, ((AssetLease<TextAsset>)lease).asset);
        AggregateException completed = Assert.Throws<AggregateException>(lease.Dispose);
        Assert.Same(ordinary, Assert.Single(completed.InnerExceptions));
        lease.Dispose();
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task ConcurrentReleaseCannotReportCompletionWhileTheProviderIsStillExecuting()
    {
        using var started = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        int attempts = 0;
        using ArtifactLease lease = new TestProvider().Artifact(Artifact(), () =>
        {
            Interlocked.Increment(ref attempts);
            started.Set();
            Assert.True(finish.Wait(TimeSpan.FromSeconds(5)));
        });
        Task worker = Task.Run(lease.Dispose);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.Throws<RetirementPendingException>(lease.Dispose);
            Assert.NotNull(lease.info);
            Assert.Equal(1, attempts);
        }
        finally { finish.Set(); await worker; }
        lease.Dispose();
        Assert.Equal(1, attempts);
        Assert.Throws<ObjectDisposedException>(() => _ = lease.info);
    }

    [Fact]
    public void RetentionScopePreservesLifoDependenciesAndEarlierFailuresAcrossPending()
    {
        var order = new List<string>();
        int pendingAttempts = 0;
        var scope = new RetentionScope();
        scope.Retain(new ReleaseStep(() => order.Add("dependency")));
        scope.Retain(new ReleaseStep(() =>
        {
            order.Add("pending");
            if (++pendingAttempts == 1) throw new RetirementPendingException("Expected owner drain.");
        }));
        scope.Retain(new ReleaseStep(() => { order.Add("failure"); throw new InvalidOperationException("Expected release failure."); }));

        Assert.Throws<RetirementPendingException>(scope.Dispose);
        Assert.Equal(new[] { "failure", "pending" }, order);
        Assert.Throws<InvalidOperationException>(() => scope.Retain(new ReleaseStep(() => { })));
        AggregateException failure = Assert.Throws<AggregateException>(scope.Dispose);
        Assert.Contains(failure.InnerExceptions, exception => exception.Message == "Expected release failure.");
        Assert.Equal(new[] { "failure", "pending", "pending", "dependency" }, order);
        scope.Dispose();
        Assert.Equal(4, order.Count);
    }

    [Fact]
    public void PendingValueIsStronglyOwnedAndBecomesCollectibleOnlyAfterTerminalRelease()
    {
        ArtifactLease lease = CreateRetainedArtifact(out WeakReference value);
        Assert.Throws<RetirementPendingException>(lease.Dispose);
        Collect();
        Assert.True(value.IsAlive);
        lease.Dispose();
        Collect();
        Assert.False(value.IsAlive);
        GC.KeepAlive(lease);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ArtifactLease CreateRetainedArtifact(out WeakReference value)
    {
        AssetArtifactInfo artifact = Artifact();
        value = new WeakReference(artifact);
        int attempts = 0;
        return new TestProvider().Artifact(artifact, () =>
        {
            if (++attempts == 1) throw new RetirementPendingException("Expected retained metadata.");
        });
    }

    private static AssetArtifactInfo Artifact() => new(new AssetArtifactKey("AABB"), "audio-data",
        Path.Combine(Path.GetTempPath(), "inno-retirement-fixture.wav"), "TEST", 0);

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class TestProvider : AssetResidencyProvider
    {
        internal AssetLease<TextAsset> Asset(TextAsset asset, Action release) => CreateAssetLease(asset, release);
        internal ArtifactLease Artifact(AssetArtifactInfo info, Action release) => CreateArtifactLease(info, release);
    }

    private sealed class ReleaseStep(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
