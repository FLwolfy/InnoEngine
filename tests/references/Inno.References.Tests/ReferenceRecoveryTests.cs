using System;
using System.Collections.Generic;
using System.Threading;
using Inno.Core.Execution;
using Inno.Extensibility.Reload;
using Xunit;

namespace Inno.References.Tests;

public sealed class ReferenceRecoveryTests
{
    [Theory]
    [InlineData("prepare")]
    [InlineData("apply")]
    [InlineData("validate")]
    public void CandidateFailureCompensatesAttemptedOwnersAroundPublication(string phase)
    {
        var events = new List<string>();
        var first = new Participant("first", events);
        var second = new Participant("second", events, phase);
        var third = new Participant("third", events);
        var recovery = new ReferenceRecoveryTransaction(ReferenceCatalog.empty, [], [first, second, third]);
        var gate = new GenerationCoordinator();
        Assert.Throws<InvalidOperationException>(() => gate.Execute("recovery", new Publication(events), [recovery]));
        Assert.Equal(GenerationState.Ready, gate.state);
        int publicationRollback = events.IndexOf("publication:rollback");
        Assert.True(events.IndexOf("structure:second") < publicationRollback);
        Assert.True(events.IndexOf("restore:second") > publicationRollback);
        Assert.True(events.IndexOf("restore:first") > events.IndexOf("restore:second"));
        Assert.DoesNotContain("commit", events);
        if (phase == "prepare")
        {
            Assert.DoesNotContain("prepare:third", events);
            Assert.DoesNotContain("structure:third", events);
            Assert.DoesNotContain("restore:third", events);
        }
    }

    [Theory]
    [InlineData("complete", false)]
    [InlineData("structure", false)]
    [InlineData("restore", false)]
    [InlineData("complete", true)]
    [InlineData("structure", true)]
    [InlineData("restore", true)]
    public void PendingRetirementKeepsOwnersAndFaultsTheSharedGate(string phase, bool wrapped)
    {
        var events = new List<string>();
        var first = new Participant("first", events);
        var pending = new Participant("pending", events, phase, pending: true, wrapped: wrapped);
        var gate = new GenerationCoordinator();
        var recovery = new ReferenceRecoveryTransaction(ReferenceCatalog.empty, [],
            phase == "complete" ? [pending, first] : [first, pending]);
        Exception failure = Assert.ThrowsAny<Exception>(() => gate.Execute("recovery", new Publication(events, phase != "complete"), [recovery]));
        if (wrapped || phase != "complete") Assert.IsType<AggregateException>(failure);
        else Assert.IsType<RetirementPendingException>(failure);
        Assert.Same(failure, gate.failure);
        Assert.Equal(GenerationState.Faulted, gate.state);
        Assert.DoesNotContain($"{phase}:first", events);
        Exception retained = Assert.ThrowsAny<Exception>(recovery.Complete);
        if (phase == "complete") Assert.Same(failure, retained);
        else
        {
            Assert.Contains(retained, Assert.IsType<AggregateException>(failure).InnerExceptions);
            Assert.Contains(Assert.IsType<AggregateException>(failure).InnerExceptions,
                item => item.Message == "Rejected publication.");
        }
        Assert.Same(retained, Assert.ThrowsAny<Exception>(recovery.RollbackStructure));
        Assert.Same(retained, Assert.ThrowsAny<Exception>(recovery.RestorePreviousState));
        Assert.Throws<InvalidOperationException>(() => gate.EnsureReady("Play"));
        Assert.Throws<RetirementPendingException>(gate.EnsureRetirementSafe);
    }

    [Fact]
    public void OrdinaryCleanupFailureStillRetiresOtherOwnersAndCannotPretendToRollback()
    {
        var events = new List<string>();
        var recovery = new ReferenceRecoveryTransaction(ReferenceCatalog.empty, [],
            [new Participant("first", events, "complete"), new Participant("second", events)]);
        var gate = new GenerationCoordinator();
        Assert.Throws<AggregateException>(() => gate.Execute("recovery", new Publication(events), [recovery]));
        Assert.Contains("complete:second", events);
        Assert.DoesNotContain("publication:rollback", events);
        Assert.Equal(GenerationState.Faulted, gate.state);
        Assert.Throws<InvalidOperationException>(recovery.RollbackStructure);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("structure")]
    [InlineData("restore")]
    public void EarlierParticipantFailureIsRetainedAlongsidePendingOwnership(string phase)
    {
        var events = new List<string>();
        var ordinary = new Participant("ordinary", events, phase);
        var pending = new Participant("pending", events, phase, pending: true);
        var dependency = new Participant("dependency", events);
        var recovery = new ReferenceRecoveryTransaction(ReferenceCatalog.empty, [],
            phase == "complete" ? [ordinary, pending, dependency] : [dependency, pending, ordinary]);
        recovery.PrepareForActivation();
        Action finish;
        if (phase == "complete")
        {
            recovery.Apply();
            finish = recovery.Complete;
        }
        else if (phase == "structure") finish = recovery.RollbackStructure;
        else
        {
            recovery.RollbackStructure();
            finish = recovery.RestorePreviousState;
        }
        AggregateException failure = Assert.Throws<AggregateException>(finish);
        Assert.Contains(failure.InnerExceptions, item => item.Message == "Rejected reference owner.");
        Assert.NotNull(RetirementPendingException.Find(failure));
        Assert.DoesNotContain($"{phase}:dependency", events);
        Assert.Same(failure, Assert.Throws<AggregateException>(recovery.Complete));
    }

    [Fact]
    public void RecoveryRejectsAnotherThreadBeforeInvokingDomainCode()
    {
        var events = new List<string>();
        var recovery = new ReferenceRecoveryTransaction(ReferenceCatalog.empty, [], [new Participant("owner", events)]);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { recovery.PrepareForActivation(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.Start();
        thread.Join();
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Empty(events);
        recovery.RollbackStructure();
        recovery.RestorePreviousState();
    }

    private sealed class Participant(string name, ICollection<string> events, string? failurePhase = null,
        bool pending = false, bool wrapped = false) : IReferenceRecoveryParticipant
    {
        public void PrepareForActivation() => Run("prepare");
        public void Apply() => Run("apply");
        public void Validate(IReadOnlyList<ReferenceRecoveryChange> changes) => Run("validate");
        public void Complete() => Run("complete");
        public void RollbackStructure() => Run("structure");
        public void RestorePreviousState() => Run("restore");

        private void Run(string phase)
        {
            events.Add($"{phase}:{name}");
            if (phase != failurePhase)
                return;
            if (pending)
            {
                if (wrapped)
                    throw new AggregateException(new InvalidOperationException("completed sibling"),
                        new InvalidOperationException("owner context", new RetirementPendingException("Pending reference owner.")));
                throw new RetirementPendingException("Pending reference owner.");
            }
            throw new InvalidOperationException("Rejected reference owner.");
        }
    }

    private sealed class Publication(ICollection<string> events, bool failApply = false) : IGenerationPublication<Probe>
    {
        public void Activate()
        {
            events.Add("publication:activate");
            if (failApply)
                throw new InvalidOperationException("Rejected publication.");
        }
        public void Rollback() => events.Add("publication:rollback");
        public Probe Complete()
        {
            events.Add("commit");
            return new Probe();
        }
    }

    private sealed class Probe : IAssemblyUnloadProbe
    {
        public string description => "Pure value recovery";
        public bool isCompleted => true;
    }
}
