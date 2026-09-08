using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace Inno.Core.Execution.Tests;

public sealed class LifetimeAdmissionTests
{
    [Fact]
    public async Task CapacityRejectsBeforeUserCodeAndCompletedWorkReleasesAdmission()
    {
        using var owner = new LifetimeScope(1);
        for (int iteration = 0; iteration < 256; iteration++)
        {
            var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<int> operation = owner.RunAsync(_ => new ValueTask<int>(ready.Task));
            bool invoked = false;
            Assert.Throws<InvalidOperationException>(() => { _ = owner.RunAsync(_ => { invoked = true; return ValueTask.FromResult(0); }); });
            Assert.False(invoked);
            ready.SetResult(iteration);
            Assert.Equal(iteration, await operation);
        }
        Assert.Equal(1, owner.peakTrackedWorkCount);
        Assert.Equal(256, owner.rejectedWorkCount);
    }

    [Fact]
    public void IdleLifetimeDoesNotRetainSuccessfulOperationResults()
    {
        using var owner = new LifetimeScope();
        WeakReference result = Complete(owner);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(result.IsAlive);
        Assert.Equal(0, owner.trackedWorkCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Complete(LifetimeScope owner)
        => new(owner.RunAsync(_ => ValueTask.FromResult(new object())).GetAwaiter().GetResult());
}
