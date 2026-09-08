using System;
using System.Collections.Generic;
using Inno.Core.Identity;
using Inno.Extensibility.Reload;
using Inno.References;
using Xunit;

namespace Inno.References.Tests;

public sealed class ReferenceCatalogTests
{
    private static readonly ReferenceKindId S_KIND = new("asset");

    [Fact]
    public void Resolve_PreservesUnassignedAndMissingIntent()
    {
        var unassigned = new ReferenceDescriptor(S_KIND, Guid.Empty);
        var missing = new ReferenceDescriptor(S_KIND, Guid.NewGuid(), lastKnownName: "Portrait");

        ReferenceResolution unassignedResult = ReferenceCatalog.empty.Resolve(unassigned);
        ReferenceResolution missingResult = ReferenceCatalog.empty.Resolve(missing);

        Assert.Equal(ReferenceResolutionState.Unassigned, unassignedResult.state);
        Assert.Equal(ReferenceResolutionState.Missing, missingResult.state);
        Assert.Same(missing, missingResult.descriptor);
        Assert.Equal("Portrait", missingResult.descriptor.lastKnownName);
    }

    [Fact]
    public void Create_RejectsDuplicateReferenceKinds()
    {
        var first = new FixedResolver(S_KIND, null);
        var second = new FixedResolver(S_KIND, null);

        Assert.Throws<InvalidOperationException>(() => ReferenceCatalog.Create(1, [first, second]));
    }

    [Fact]
    public void RecoveryTransaction_RollsBackAppliedParticipantsInReverseOrder()
    {
        var allocator = new IdentityAllocator();
        var target = new Target();
        Assert.True(allocator.Register(target));
        var descriptor = new ReferenceDescriptor(S_KIND, target.identity.persistentId);
        var catalog = ReferenceCatalog.Create(
            1,
            [new FixedResolver(S_KIND, target.identity.runtimeIdentity)]);
        var events = new List<string>();
        var first = new RecordingParticipant("first", events);
        var second = new RecordingParticipant("second", events, failApply: true);
        var state = new SerializedMissingState(
            new ReferenceKey(Guid.NewGuid(), "portrait"),
            descriptor,
            [1, 2, 3]);

        var transaction = new ReferenceRecoveryTransaction(catalog, [state], [first, second]);
        transaction.PrepareForActivation();
        Assert.Throws<InvalidOperationException>(() => transaction.Apply());
        Assert.DoesNotContain("rollback:first", events);
        transaction.RollbackStructure();
        events.Add("publication:rollback");
        transaction.RestorePreviousState();

        Assert.Equal(["prepare:first", "prepare:second", "apply:first", "apply:second",
            "rollback:second", "rollback:first", "publication:rollback", "restore:second", "restore:first"], events);
        Assert.Empty(transaction.changes);
    }

    [Fact]
    public void RecoveryResolvesOnlyAfterObjectsExistAndRetainsNeutralResultsAfterCommit()
    {
        var allocator = new IdentityAllocator();
        var target = new Target();
        allocator.Register(target);
        var descriptor = new ReferenceDescriptor(S_KIND, target.identity.persistentId);
        var catalog = ReferenceCatalog.Create(1, [new FixedResolver(S_KIND, target.identity.runtimeIdentity)]);
        var events = new List<string>();
        var state = new SerializedMissingState(new ReferenceKey(Guid.NewGuid(), "asset"), descriptor, [1]);
        var transaction = new ReferenceRecoveryTransaction(catalog, [state], [new RecordingParticipant("owner", events)]);
        Assert.Empty(events);
        Assert.Empty(transaction.changes);
        transaction.PrepareForActivation();
        transaction.Apply();
        transaction.Complete();
        Assert.Equal(["prepare:owner", "apply:owner", "validate:owner", "complete:owner"], events);
        Assert.Equal(ReferenceResolutionState.Resolved, transaction.changes[0].resolution.state);
        Assert.Equal(target.identity.runtimeIdentity, transaction.changes[0].resolution.runtimeIdentity);
        Assert.Throws<InvalidOperationException>(transaction.RollbackStructure);
    }

    [Fact]
    public void RecoveryRejectsDuplicateSlotsAndOutOfOrderPhases()
    {
        var state = new SerializedMissingState(new ReferenceKey(Guid.NewGuid(), "asset"),
            new ReferenceDescriptor(S_KIND, Guid.NewGuid()), []);
        Assert.Throws<ArgumentException>(() => new ReferenceRecoveryTransaction(ReferenceCatalog.empty, [state, state], []));
        var transaction = new ReferenceRecoveryTransaction(ReferenceCatalog.empty, [state], []);
        Assert.Throws<InvalidOperationException>(transaction.Apply);
        Assert.Throws<InvalidOperationException>(transaction.Complete);
        Assert.Throws<InvalidOperationException>(transaction.RestorePreviousState);
        transaction.RollbackStructure();
        transaction.RestorePreviousState();
    }

    private sealed class Target : IdentityObject
    {
    }

    private sealed class FixedResolver(ReferenceKindId kindId, RuntimeIdentity? runtimeIdentity) : IReferenceResolver
    {
        public ReferenceKindId kindId { get; } = kindId;

        public ReferenceResolution Resolve(ReferenceDescriptor descriptor)
            => runtimeIdentity.HasValue
                ? new ReferenceResolution(descriptor, ReferenceResolutionState.Resolved, runtimeIdentity)
                : new ReferenceResolution(descriptor, ReferenceResolutionState.Missing);
    }

    private sealed class RecordingParticipant(
        string name,
        ICollection<string> events,
        bool failApply = false) : IReferenceRecoveryParticipant
    {
        public void Validate(IReadOnlyList<ReferenceRecoveryChange> changes)
            => events.Add($"validate:{name}");

        public void PrepareForActivation() => events.Add($"prepare:{name}");

        public void Apply()
        {
            events.Add($"apply:{name}");
            if (failApply)
                throw new InvalidOperationException("expected apply failure");
        }

        public void RollbackStructure()
            => events.Add($"rollback:{name}");

        public void RestorePreviousState() => events.Add($"restore:{name}");
        public void Complete() => events.Add($"complete:{name}");
    }
}
