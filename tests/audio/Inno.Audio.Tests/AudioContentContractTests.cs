using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Inno.References;
using Xunit;

namespace Inno.Audio.Tests;

public sealed class AudioContentContractTests
{
    [Fact]
    public void CombinedCapacityRejectsTheEntireContributionIncludingEarlierSnapshots()
    {
        using ContentReadScope content = ContentReadScope.empty;
        using var context = new AudioContentProviderContext(content, 0, capacity: 1);
        context.Submit(new AudioEmitterSnapshot(Guid.NewGuid(), new AudioClipAsset(), AudioPlayOptions.defaultValue, true));
        Assert.Throws<InvalidOperationException>(() => context.Submit(Listener()));
        Assert.Throws<InvalidOperationException>(() => _ = context.emitters);
        Assert.Throws<InvalidOperationException>(() => _ = context.listeners);
        Assert.Throws<InvalidOperationException>(() => context.Submit(Listener()));
    }

    [Fact]
    public void ZeroCapacityAllowsOnlyAnEmptyContribution()
    {
        using ContentReadScope content = ContentReadScope.empty;
        using var context = new AudioContentProviderContext(content, 0, capacity: 0);
        Assert.Empty(context.emitters);
        Assert.Empty(context.listeners);
        Assert.Throws<InvalidOperationException>(() => context.Submit(Listener()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefaultSnapshotsPoisonTheContribution(bool emitter)
    {
        using ContentReadScope content = ContentReadScope.empty;
        using var context = new AudioContentProviderContext(content, 0);
        if (emitter)
            Assert.Throws<ArgumentException>(() => context.Submit(default(AudioEmitterSnapshot)));
        else
            Assert.Throws<ArgumentException>(() => context.Submit(default(AudioListenerSnapshot)));
        Assert.Throws<InvalidOperationException>(() => _ = context.listeners);
    }

    [Fact]
    public void EmitterAndListenerIdentitiesShareOneAdmissionNamespace()
    {
        using ContentReadScope content = ContentReadScope.empty;
        using var context = new AudioContentProviderContext(content, 0);
        Guid id = Guid.NewGuid();
        context.Submit(new AudioEmitterSnapshot(id, new AudioClipAsset(), AudioPlayOptions.defaultValue, true));
        Assert.Throws<ArgumentException>(() => context.Submit(new AudioListenerSnapshot(id, 0, default, true)));
        Assert.Throws<InvalidOperationException>(() => _ = context.emitters);
    }

    [Fact]
    public void SnapshotsAreFrozenAndDisposalRevokesContextButDoesNotOwnContentScope()
    {
        using ContentReadScope content = ContentReadScope.empty;
        using var context = new AudioContentProviderContext(content, 0);
        context.Submit(Listener());
        IReadOnlyList<AudioListenerSnapshot> snapshot = context.listeners;
        context.Submit(Listener());
        Assert.Single(snapshot);
        Assert.Throws<NotSupportedException>(() => ((IList<AudioListenerSnapshot>)snapshot).Clear());
        context.Dispose();
        context.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = context.content);
        Assert.Throws<ObjectDisposedException>(() => _ = context.listeners);
        Assert.Throws<ObjectDisposedException>(() => context.Submit(Listener()));
        Assert.Empty(content.GetValues<AudioClipAsset>());
    }

    [Fact]
    public void CollectionAndDisposalCannotCrossTheOwnerThread()
    {
        using ContentReadScope content = ContentReadScope.empty;
        using var context = new AudioContentProviderContext(content, 0);
        Exception? submission = null;
        Exception? disposal = null;
        var worker = new Thread(() =>
        {
            submission = Record.Exception(() => context.Submit(Listener()));
            disposal = Record.Exception(context.Dispose);
        });
        worker.Start();
        worker.Join();
        Assert.IsType<InvalidOperationException>(submission);
        Assert.IsType<InvalidOperationException>(disposal);
        Assert.Empty(context.listeners);
        context.Submit(Listener());
        Assert.Single(context.listeners);
    }

    [Fact]
    public void RetainingARevokedContextDoesNotRetainItsCollectedClip()
    {
        AudioContentProviderContext context = CreateRevokedContext(out WeakReference clip);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(clip.IsAlive);
        Assert.Throws<ObjectDisposedException>(() => _ = context.emitters);
    }

    [Fact]
    public void NegativeCapacityIsRejectedAtConstruction()
    {
        using ContentReadScope content = ContentReadScope.empty;
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioContentProviderContext(content, 0, capacity: -1));
    }

    private static AudioListenerSnapshot Listener() => new(Guid.NewGuid(), 0, default, true);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AudioContentProviderContext CreateRevokedContext(out WeakReference clipReference)
    {
        using ContentReadScope content = ContentReadScope.empty;
        var context = new AudioContentProviderContext(content, 0);
        var clip = new AudioClipAsset();
        clipReference = new WeakReference(clip);
        context.Submit(new AudioEmitterSnapshot(Guid.NewGuid(), clip, AudioPlayOptions.defaultValue, true));
        context.Dispose();
        return context;
    }
}
