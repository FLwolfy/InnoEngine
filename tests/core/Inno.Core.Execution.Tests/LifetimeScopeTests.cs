using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xunit;

namespace Inno.Core.Execution.Tests;

public sealed class LifetimeScopeTests
{
    [Fact]
    public void FailureBeforePendingRemainsObservableAfterRetry()
    {
        bool ready = false;
        int failingReleaseCount = 0;
        var lifetime = new LifetimeScope();
        lifetime.Own(new Resource(() =>
        {
            if (!ready)
                throw new RetirementPendingException("pending");
        }));
        lifetime.Own(new Resource(() => { failingReleaseCount++; throw new InvalidOperationException("release failed"); }));
        Assert.Throws<RetirementPendingException>(lifetime.Dispose);
        ready = true;
        AggregateException error = Assert.Throws<AggregateException>(lifetime.Dispose);
        Assert.Contains("release failed", error.ToString());
        Assert.Equal(1, failingReleaseCount);
        lifetime.Dispose();
    }

    [Fact]
    public void RetirementDeadlineRemainsFaultedAndDoesNotReleasePendingDependencies()
    {
        var barrier = new RetirementBarrier("test owner", TimeSpan.FromTicks(1));
        Assert.Throws<RetirementTimeoutException>(() => barrier.TryComplete(
            () => throw new RetirementPendingException("worker still running")));
        bool released = false;
        RetirementTimeoutException error = Assert.Throws<RetirementTimeoutException>(
            () => barrier.TryComplete(() => released = true));
        Assert.Contains("test owner", error.Message);
        Assert.False(released);
    }

    [Fact]
    public void RetirementBarrierRetriesOnOwnerThreadAndDoesNotRetainCompletedCallback()
    {
        var barrier = new RetirementBarrier("test owner");
        Assert.False(barrier.TryComplete(() => throw new RetirementPendingException("pending")));
        Assert.True(barrier.TryComplete(() => { }));
        Assert.True(barrier.TryComplete(() => throw new InvalidOperationException("must not run")));
    }

    [Fact]
    public void RetirementAttemptsEveryResourceInReverseOrder()
    {
        List<int> released = [];
        var lifetime = new LifetimeScope();
        lifetime.Own(new Resource(() => released.Add(1)));
        lifetime.Own(new Resource(() => { released.Add(2); throw new InvalidOperationException("expected"); }));
        lifetime.Own(new Resource(() => released.Add(3)));
        Assert.Throws<AggregateException>(lifetime.Dispose);
        Assert.Equal(new[] { 3, 2, 1 }, released);
        lifetime.Dispose();
    }

    [Fact]
    public async Task AsyncRetirementCancelsAndDrainsBeforeRelease()
    {
        var lifetime = new LifetimeScope();
        bool released = false;
        lifetime.Own(new Resource(() => released = true));
        lifetime.Track(Task.Delay(-1, lifetime.cancellationToken));
        await lifetime.DisposeAsync();
        Assert.True(released);
        Assert.True(lifetime.isQuiescent);
    }

    [Fact]
    public void PendingWorkBlocksSynchronousReleaseWithoutLosingOwnership()
    {
        var lifetime = new LifetimeScope();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool released = false;
        lifetime.Own(new Resource(() => released = true));
        lifetime.Track(pending.Task);
        Assert.Throws<RetirementPendingException>(lifetime.Dispose);
        Assert.False(released);
        pending.SetResult();
        lifetime.Dispose();
        Assert.True(released);
    }

    private sealed class Resource(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
