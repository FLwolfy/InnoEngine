using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Inno.Core.Execution.Tests;

public sealed class RetirementFailureTests
{
    [Fact]
    public void ClassificationFindsNestedTimeoutWithoutReplacingTheOriginalTree()
    {
        var pending = new RetirementPendingException("waiting");
        var timeout = new RetirementTimeoutException("expired");
        var ordinary = new InvalidOperationException("completed failure");
        var wrapped = new InvalidOperationException("context", new AggregateException(ordinary, pending, timeout));
        Assert.Same(timeout, RetirementPendingException.Find(wrapped));
        Assert.Same(timeout, RetirementPendingException.Find(new RetirementPendingException("outer", wrapped)));
        Assert.Null(RetirementPendingException.Find(ordinary));
        Assert.Throws<ArgumentNullException>(() => RetirementPendingException.Find(null!));
        var failures = new List<Exception>();
        RetirementPendingException.CollectCompletedFailures(wrapped, failures);
        RetirementPendingException.CollectCompletedFailures(wrapped, failures);
        Assert.Same(ordinary, Assert.Single(failures));
    }

    [Fact]
    public void LifetimeRetainsPendingResourceAndEarlierErrorsAcrossWrappedRetries()
    {
        var lifetime = new LifetimeScope();
        bool ready = false;
        bool dependencyReleased = false;
        var ordinary = new InvalidOperationException("completed sibling");
        var failure = new InvalidOperationException("resource context", new AggregateException(
            ordinary, new AggregateException(new RetirementPendingException("callback remains active"))));
        lifetime.Own(new Resource(() => dependencyReleased = true));
        lifetime.Own(new Resource(() => { if (!ready) throw failure; }));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(lifetime.Dispose));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(lifetime.Dispose));
        Assert.False(dependencyReleased);
        ready = true;
        AggregateException completed = Assert.Throws<AggregateException>(lifetime.Dispose);
        Assert.Same(ordinary, Assert.Single(completed.Flatten().InnerExceptions));
        Assert.True(dependencyReleased);
        lifetime.Dispose();
    }

    [Fact]
    public void BarrierRetainsOrdinarySiblingsUntilThePendingWorkCompletes()
    {
        var barrier = new RetirementBarrier("wrapped callback");
        var ordinary = new InvalidOperationException("completed sibling");
        var failure = new AggregateException(ordinary, new RetirementPendingException("pending"));
        Assert.False(barrier.TryComplete(() => throw failure));
        Assert.False(barrier.TryComplete(() => throw failure));
        AggregateException completed = Assert.Throws<AggregateException>(() => barrier.TryComplete(() => { }));
        Assert.Same(ordinary, Assert.Single(completed.InnerExceptions));
        Assert.True(barrier.TryComplete(() => throw new InvalidOperationException("must not run twice")));
    }

    [Fact]
    public void NestedTimeoutRemainsTerminalAndPreservesEveryFailure()
    {
        var barrier = new RetirementBarrier("native callback");
        var timeout = new RetirementTimeoutException("inner deadline");
        var failure = new AggregateException(new InvalidOperationException("first"), timeout);
        Assert.Same(failure, Assert.Throws<AggregateException>(() => barrier.TryComplete(() => throw failure)));
        Assert.Same(failure, Assert.Throws<AggregateException>(() => barrier.TryComplete(() => { })));
    }

    [Fact]
    public void NewTimeoutKeepsTheCompleteWrappedCause()
    {
        var barrier = new RetirementBarrier("deadline", TimeSpan.FromTicks(1));
        var failure = new InvalidOperationException("context", new RetirementPendingException("pending callback"));
        RetirementTimeoutException timeout = Assert.Throws<RetirementTimeoutException>(
            () => barrier.TryComplete(() => throw failure));
        Assert.Same(failure, timeout.InnerException);
        Assert.Same(timeout, Assert.Throws<RetirementTimeoutException>(() => barrier.TryComplete(() => { })));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedTaskOrCancellationPendingDoesNotAuthorizeDependencyRelease(bool cancellation)
    {
        var lifetime = new LifetimeScope();
        bool dependencyReleased = false;
        lifetime.Own(new Resource(() => dependencyReleased = true));
        var failure = new AggregateException(new RetirementPendingException("unfinished owner outside completed task"));
        if (cancellation)
            lifetime.cancellationToken.Register(() => throw failure);
        else
            lifetime.Track(Task.FromException(failure));
        Exception first = Assert.ThrowsAny<Exception>(lifetime.Dispose);
        Assert.NotNull(RetirementPendingException.Find(first));
        Assert.Same(first, Assert.ThrowsAny<Exception>(lifetime.Dispose));
        Assert.Same(first, await Assert.ThrowsAnyAsync<Exception>(async () => await lifetime.DisposeAsync()));
        Assert.False(lifetime.isQuiescent);
        Assert.False(dependencyReleased);
    }

    [Fact]
    public void DirectCancellationCannotLoseItsPendingFailureBeforeDisposal()
    {
        var lifetime = new LifetimeScope();
        bool released = false;
        lifetime.Own(new Resource(() => released = true));
        lifetime.cancellationToken.Register(() => throw new RetirementPendingException("callback owns active work"));
        AggregateException failure = Assert.Throws<AggregateException>(lifetime.Cancel);
        Assert.False(lifetime.isQuiescent);
        Assert.Same(failure, Assert.Throws<AggregateException>(lifetime.Cancel));
        Assert.Same(failure, Assert.Throws<AggregateException>(lifetime.Dispose));
        Assert.False(released);
    }

    [Fact]
    public async Task ConcurrentCancellationKeepsDependenciesUntilCallbacksReturn()
    {
        using var started = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var lifetime = new LifetimeScope();
        bool released = false;
        lifetime.Own(new Resource(() => released = true));
        lifetime.cancellationToken.Register(() =>
        {
            started.Set();
            Assert.True(finish.Wait(TimeSpan.FromSeconds(5)));
        });
        Task cancellation = Task.Run(lifetime.Cancel);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(lifetime.isQuiescent);
            Assert.Throws<RetirementPendingException>(lifetime.Dispose);
            Assert.False(released);
        }
        finally
        {
            finish.Set();
            await cancellation;
        }
        lifetime.Dispose();
        Assert.True(released);
        Assert.True(lifetime.isQuiescent);
    }

    [Fact]
    public void DirectCancellationRetainsOrdinaryErrorsUntilResourcesRetire()
    {
        var lifetime = new LifetimeScope();
        var failure = new InvalidOperationException("cancellation failed");
        bool released = false;
        lifetime.Own(new Resource(() => released = true));
        lifetime.cancellationToken.Register(() => throw failure);
        Assert.Throws<AggregateException>(lifetime.Cancel);
        AggregateException reported = Assert.Throws<AggregateException>(lifetime.Dispose);
        Assert.Same(failure, Assert.Single(reported.Flatten().InnerExceptions));
        Assert.True(released);
        lifetime.Dispose();
    }

    [Fact]
    public async Task ConcurrentDisposalCannotReleaseTheSameResourceTwice()
    {
        using var started = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var lifetime = new LifetimeScope();
        int released = 0;
        lifetime.Own(new Resource(() =>
        {
            Interlocked.Increment(ref released);
            started.Set();
            Assert.True(finish.Wait(TimeSpan.FromSeconds(5)));
        }));
        Task disposal = Task.Run(lifetime.Dispose);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(lifetime.isQuiescent);
            Assert.Throws<RetirementPendingException>(lifetime.Dispose);
            Assert.Equal(1, released);
        }
        finally
        {
            finish.Set();
            await disposal;
        }
        lifetime.Dispose();
        Assert.True(lifetime.isQuiescent);
        Assert.Equal(1, released);
    }

    [Fact]
    public void ReentrantDisposalKeepsTheCurrentOwnerUntilItsCallbackReturns()
    {
        var lifetime = new LifetimeScope();
        int released = 0;
        lifetime.Own(new Resource(() =>
        {
            Assert.Throws<RetirementPendingException>(lifetime.Dispose);
            released++;
        }));
        lifetime.Dispose();
        Assert.Equal(1, released);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncOperationIsOwnedBeforeItsSynchronousPrefixCanReenter(bool dispose)
    {
        var lifetime = new LifetimeScope();
        bool released = false;
        lifetime.Own(new Resource(() => released = true));
        Task<int> operation = lifetime.RunAsync<int>(_ =>
        {
            Assert.False(lifetime.isQuiescent);
            if (dispose)
                Assert.Throws<RetirementPendingException>(lifetime.Dispose);
            else lifetime.Cancel();
            Assert.False(released);
            return ValueTask.FromResult(42);
        });
        Assert.Equal(42, await operation);
        lifetime.Dispose();
        Assert.True(released);
    }

    [Fact]
    public async Task CancellationWrapperCannotErasePendingOperationOwnership()
    {
        var lifetime = new LifetimeScope();
        bool released = false;
        lifetime.Own(new Resource(() => released = true));
        var pending = new RetirementPendingException("work remains outside cancellation");
        var failure = new OperationCanceledException("cancellation context", pending);
        Task<int> operation = lifetime.RunAsync<int>(_ => throw failure);
        Assert.Same(failure, await Assert.ThrowsAsync<OperationCanceledException>(async () => await operation));
        Assert.True(operation.IsFaulted);
        Assert.False(operation.IsCanceled);
        Assert.Same(pending, RetirementPendingException.Find(Assert.Throws<AggregateException>(lifetime.Dispose)));
        Assert.False(released);
    }

    private sealed class Resource(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
