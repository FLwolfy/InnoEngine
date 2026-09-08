using System;
using System.Threading;
using Inno.Extensibility.Reload;
using Xunit;

namespace Inno.Extensibility.Reload.Tests;

public sealed class AssemblyUnloadBarrierTests
{
    [Fact]
    public void Advance_CompletesOnlyAfterEveryProbeCompletes()
    {
        var first = new Probe("first");
        var second = new Probe("second");
        var barrier = new AssemblyUnloadBarrier(
            [first, second],
            new AssemblyUnloadBarrierOptions
            {
                collectionInterval = TimeSpan.Zero,
                retentionTimeout = TimeSpan.FromSeconds(2)
            });

        first.isCompleted = true;
        Assert.False(barrier.Advance());
        Assert.Equal(AssemblyUnloadBarrierState.AwaitingCollection, barrier.state);

        second.isCompleted = true;
        Assert.True(barrier.Advance());
        Assert.Equal(AssemblyUnloadBarrierState.Completed, barrier.state);
    }

    [Fact]
    public void Advance_RetainsFaultAndRejectsEveryLaterSuccessAttempt()
    {
        var probe = new Probe("Plugin.Game (InnoPlugin/Runtime, generation 4)");
        var barrier = new AssemblyUnloadBarrier(
            [probe],
            new AssemblyUnloadBarrierOptions
            {
                collectionInterval = TimeSpan.Zero,
                retentionTimeout = TimeSpan.FromMilliseconds(20)
            });

        Thread.Sleep(30);
        AssemblyUnloadException first = Assert.Throws<AssemblyUnloadException>(() => barrier.Advance());
        AssemblyUnloadException second = Assert.Throws<AssemblyUnloadException>(() => barrier.Advance());

        Assert.Same(first, second);
        Assert.Equal(AssemblyUnloadBarrierState.Faulted, barrier.state);
        Assert.Contains("Plugin.Game", first.Message, StringComparison.Ordinal);
        Assert.NotEmpty(first.retainedGenerations);
    }

    private sealed class Probe(string description) : IAssemblyUnloadProbe
    {
        public string description { get; } = description;

        public bool isCompleted { get; set; }
    }
}
