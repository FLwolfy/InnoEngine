using System;
using System.Collections.Generic;
using System.Threading;
using Inno.Core.Execution;
using Inno.Extensibility.Reload;
using Xunit;

namespace Inno.Extensibility.Reload.Tests;

public sealed class GenerationCoordinatorTests
{
    [Fact]
    public void CommitDoesNotReopenAdmissionUntilAllRetirementsComplete()
    {
        var owner = new GenerationCoordinator();
        var probe = new Probe();
        var events = new List<string>();
        owner.Execute("reload", new Publication(probe, events), [new Change("scene", events)]);
        Assert.Equal(["prepare:scene", "activate", "apply:scene", "commit", "complete:scene"], events);
        Assert.Equal(GenerationState.AwaitingCollection, owner.state);
        Assert.Throws<InvalidOperationException>(() => owner.EnsureReady("Play"));
        Assert.False(owner.Advance());
        probe.isCompleted = true;
        Assert.True(owner.Advance());
        owner.EnsureReady("Play");
    }

    [Fact]
    public void FailedApplyRollsBackStructureBeforePublicationAndThenRestoresValues()
    {
        var owner = new GenerationCoordinator();
        var events = new List<string>();
        Assert.Throws<InvalidOperationException>(() => owner.Execute("reload",
            new Publication(new Probe { isCompleted = true }, events),
            [new Change("first", events), new Change("second", events, failApply: true)]));
        Assert.Equal(["prepare:first", "prepare:second", "activate", "apply:first", "apply:second",
            "structure:second", "structure:first", "rollback", "restore:second", "restore:first"], events);
        Assert.Equal(GenerationState.Ready, owner.state);
    }

    [Fact]
    public void IrreversibleCleanupFailureFaultsOwnerAndStillAttemptsAllCleanup()
    {
        var owner = new GenerationCoordinator();
        var events = new List<string>();
        Assert.Throws<AggregateException>(() => owner.Execute("reload",
            new Publication(new Probe { isCompleted = true }, events),
            [new Change("first", events, failComplete: true), new Change("second", events)]));
        Assert.Contains("complete:second", events);
        Assert.DoesNotContain("rollback", events);
        Assert.Equal(GenerationState.Faulted, owner.state);
        Assert.Throws<InvalidOperationException>(() => owner.EnsureReady("Export"));
    }

    [Fact]
    public void BuildReadLeaseBlocksGenerationMutationUntilDisposed()
    {
        var owner = new GenerationCoordinator();
        IDisposable build = owner.AcquireRead("Build");
        Assert.Throws<InvalidOperationException>(() => owner.EnsureReady("reload"));
        build.Dispose();
        build.Dispose();
        owner.EnsureReady("reload");
    }

    [Fact]
    public void RollbackFailureIsTerminalEvenWhenNoCollectibleMonitorExists()
    {
        var owner = new GenerationCoordinator();
        var events = new List<string>();
        Assert.Throws<AggregateException>(() => owner.Execute("reload",
            new Publication(new Probe { isCompleted = true }, events),
            [new Change("scene", events, failApply: true, failRestore: true)]));
        Assert.Equal(GenerationState.Faulted, owner.state);
        Assert.Throws<InvalidOperationException>(() => owner.Advance());
    }

    [Fact]
    public void AutomaticRefreshDefersDuringBuildAndExcludesNewReadersWhilePublishing()
    {
        var owner = new GenerationCoordinator();
        using (owner.AcquireRead("build"))
        {
            Assert.False(owner.TryAcquireChange("refresh", out IDisposable? blocked));
            Assert.Null(blocked);
        }
        Assert.True(owner.TryAcquireChange("refresh", out IDisposable? publication));
        Assert.Throws<InvalidOperationException>(() => owner.AcquireRead("export"));
        publication!.Dispose();
        Assert.Equal(GenerationState.Ready, owner.state);
        Assert.True(owner.TryAcquireChange("refresh", out IDisposable? failedPublication));
        owner.Fault(new InvalidOperationException("cleanup"));
        failedPublication!.Dispose();
        Assert.Equal(GenerationState.Faulted, owner.state);
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("complete")]
    [InlineData("rollback")]
    public void PendingRetirementNeverFallsThroughToLowerCleanupOrReopensAdmission(string phase)
    {
        var owner = new GenerationCoordinator();
        var events = new List<string>();
        var failure = new RetirementTimeoutException("Pending generation-owned work exceeded its deadline.");
        var change = new PendingChange(phase, events, failure);
        Exception reported = Assert.ThrowsAny<Exception>(() => owner.Execute("reload",
            new Publication(new Probe { isCompleted = true }, events), [change]));
        Assert.Same(failure, RetirementPendingException.Find(reported));
        if (phase == "rollback")
            Assert.Contains(Assert.IsType<AggregateException>(reported).InnerExceptions,
                item => item.Message == "Candidate publication failed.");
        else Assert.Same(failure, reported);
        Assert.DoesNotContain("rollback", events);
        Assert.DoesNotContain("restore", events);
        Assert.Equal(GenerationState.Faulted, owner.state);
        Assert.Same(reported, owner.failure);
        Assert.Throws<InvalidOperationException>(() => owner.EnsureReady("Play"));
        Assert.Throws<InvalidOperationException>(() => owner.AcquireRead("Build"));
        Assert.Throws<InvalidOperationException>(() => owner.TryAcquireChange("reload", out _));
    }

    [Fact]
    public void PublishedOperationDefersAutomaticReplacementUntilTheWholeScopeEnds()
    {
        var owner = new GenerationCoordinator();
        IDisposable operation = owner.AcquireOperation("Asset mutation");
        Assert.False(owner.TryAcquireChange("automatic refresh", out _));
        Assert.Throws<InvalidOperationException>(() => owner.EnsureReady("Play"));
        operation.Dispose();
        operation.Dispose();
        Assert.True(owner.TryAcquireChange("automatic refresh", out IDisposable? publication));
        publication!.Dispose();
        Assert.Equal(GenerationState.Ready, owner.state);
    }

    [Fact]
    public void PublicationMayBorrowAnOperationOnlyOnItsOwnControlThread()
    {
        var owner = new GenerationCoordinator();
        Assert.True(owner.TryAcquireChange("refresh", out IDisposable? publication));
        using (owner.AcquireOperation("validate candidate"))
            Assert.Throws<InvalidOperationException>(() => owner.AcquireRead("Build"));
        Assert.Equal(GenerationState.Transitioning, owner.state);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using IDisposable operation = owner.AcquireOperation("background Asset query");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(failure);
        publication!.Dispose();
        owner.EnsureReady("Play");
    }

    [Fact]
    public void PublishedOperationDoesNotAuthorizeNewWorkWhileOldContextsAwaitCollection()
    {
        var owner = new GenerationCoordinator();
        var probe = new Probe();
        owner.TrackRetirement(probe);
        using (owner.AcquireOperation("read published Asset"))
        {
            Assert.Equal(GenerationState.AwaitingCollection, owner.state);
            Assert.False(owner.TryAcquireChange("reload", out _));
            Assert.Throws<InvalidOperationException>(() => owner.AcquireRead("Export"));
            Assert.Throws<InvalidOperationException>(() => owner.EnsureReady("Play"));
        }
        Assert.Equal(GenerationState.AwaitingCollection, owner.state);
        probe.isCompleted = true;
        Assert.True(owner.Advance());
        owner.EnsureReady("Play");
    }

    [Fact]
    public void ExecuteBorrowsOperationAdmissionWithoutReopeningItBetweenDomainPhases()
    {
        var owner = new GenerationCoordinator();
        var events = new List<string>();
        owner.Execute("reload", new Publication(new Probe { isCompleted = true }, events),
            [new OperationChange(owner, events)]);
        Assert.Equal(["prepare", "activate", "apply", "commit", "complete"], events);
        owner.EnsureReady("Play");
        owner.Fault(new InvalidOperationException("Terminal cleanup failure."));
        Assert.Throws<InvalidOperationException>(() => owner.AcquireOperation("Asset query"));
    }

    [Fact]
    public void EarlierOrdinaryFailureDoesNotHideThePendingDependencyBarrier()
    {
        var owner = new GenerationCoordinator();
        var first = new InvalidOperationException("Completed cleanup failed.");
        owner.Fault(first);
        owner.EnsureRetirementSafe();
        var pending = new RetirementTimeoutException("Unfinished callback.");
        owner.Fault(new AggregateException("Startup and retirement failed.", first, pending));
        Assert.Same(first, owner.failure);
        Assert.Same(pending, Assert.Throws<RetirementTimeoutException>(owner.EnsureRetirementSafe));
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("apply")]
    [InlineData("complete")]
    [InlineData("rollback")]
    [InlineData("restore")]
    public void WrappedPendingRetainsTheTransactionWithoutContinuingDependentPhases(string phase)
    {
        var owner = new GenerationCoordinator();
        var events = new List<string>();
        var pending = new RetirementTimeoutException("unfinished native callback");
        var failure = new AggregateException(new InvalidOperationException("completed sibling"),
            new InvalidOperationException("scope context", pending));
        var change = new WrappedChange(phase, failure, events);
        AggregateException reported = Assert.Throws<AggregateException>(() => owner.Execute("reload",
            new Publication(new Probe { isCompleted = true }, events), [change]));
        if (phase is "rollback" or "restore")
        {
            Assert.Contains(failure, reported.InnerExceptions);
            Assert.Contains(reported.InnerExceptions, item => item.Message == "activation rejected");
        }
        else Assert.Same(failure, reported);
        Assert.Equal(GenerationState.Faulted, owner.state);
        Assert.Same(reported, owner.failure);
        Assert.Same(pending, Assert.Throws<RetirementTimeoutException>(owner.EnsureRetirementSafe));
        if (phase is "prepare" or "apply" or "complete") Assert.DoesNotContain("rollback", events);
        if (phase != "restore") Assert.DoesNotContain("restore", events);
    }

    [Fact]
    public void CleanupFailureBeforeAPendingParticipantRemainsInTheTerminalReport()
    {
        var owner = new GenerationCoordinator();
        var events = new List<string>();
        var pending = new RetirementPendingException("unfinished callback");
        AggregateException failure = Assert.Throws<AggregateException>(() => owner.Execute("reload",
            new Publication(new Probe { isCompleted = true }, events),
            [new Change("ordinary", events, failComplete: true), new WrappedChange("complete", pending, events),
                new Change("dependency", events)]));
        Assert.Same(failure, owner.failure);
        Assert.Contains(failure.InnerExceptions, item => item.Message == "complete");
        Assert.Contains(pending, failure.InnerExceptions);
        Assert.DoesNotContain("complete:dependency", events);
        Assert.Same(pending, Assert.Throws<RetirementPendingException>(owner.EnsureRetirementSafe));
    }

    private sealed class PendingChange(string phase, List<string> events, RetirementTimeoutException failure)
        : IGenerationChange
    {
        public void PrepareForActivation()
        {
            events.Add("prepare");
            if (phase == "prepare")
                throw failure;
        }

        public void Apply()
        {
            if (phase == "rollback")
                throw new InvalidOperationException("Candidate publication failed.");
        }

        public void Complete() => throw failure;

        public void RollbackStructure() => throw failure;

        public void RestorePreviousState() => events.Add("restore");
    }

    private sealed class WrappedChange(string phase, Exception failure, List<string> events) : IGenerationChange
    {
        public void PrepareForActivation() => Visit("prepare");
        public void Apply()
        {
            Visit("apply");
            if (phase is "rollback" or "restore") throw new InvalidOperationException("activation rejected");
        }
        public void Complete() => Visit("complete");
        public void RollbackStructure() => Visit("rollback");
        public void RestorePreviousState() => Visit("restore");
        private void Visit(string current)
        {
            events.Add(current);
            if (phase == current) throw failure;
        }
    }

    private sealed class Probe : IAssemblyUnloadProbe
    {
        public string description => "Plugin.Test / Runtime / generation 9";
        public bool isCompleted { get; set; }
    }

    private sealed class OperationChange(GenerationCoordinator owner, List<string> events) : IGenerationChange
    {
        public void PrepareForActivation() => Visit("prepare");
        public void Apply() => Visit("apply");
        public void Complete() => Visit("complete");
        public void RollbackStructure() => Visit("rollback");
        public void RestorePreviousState() => Visit("restore");

        private void Visit(string phase)
        {
            using IDisposable operation = owner.AcquireOperation(phase);
            Assert.Equal(GenerationState.Transitioning, owner.state);
            Assert.False(owner.TryAcquireChange("nested refresh", out _));
            events.Add(phase);
        }
    }

    private sealed class Publication(Probe probe, List<string> events) : IGenerationPublication<Probe>
    {
        public void Activate() => events.Add("activate");
        public void Rollback() => events.Add("rollback");
        public Probe Complete() { events.Add("commit"); return probe; }
    }

    private sealed class Change(string name, List<string> events,
        bool failApply = false, bool failComplete = false, bool failRestore = false) : IGenerationChange
    {
        public void PrepareForActivation() => events.Add($"prepare:{name}");
        public void Apply() { events.Add($"apply:{name}"); if (failApply) throw new InvalidOperationException("apply"); }
        public void Complete() { events.Add($"complete:{name}"); if (failComplete) throw new InvalidOperationException("complete"); }
        public void RollbackStructure() => events.Add($"structure:{name}");
        public void RestorePreviousState() { events.Add($"restore:{name}"); if (failRestore) throw new InvalidOperationException("restore"); }
    }
}
