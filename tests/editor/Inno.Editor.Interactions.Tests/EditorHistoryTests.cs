using System;
using System.Collections.Generic;
using System.IO;

using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Xunit;

namespace Inno.Editor.Interactions.Tests;

public sealed class EditorHistoryTests
{
    [Fact]
    public void ExecuteUndoAndRedoMoveNeutralChangeBetweenStacks()
    {
        using var fixture = new HistoryFixture();

        EditorHistoryResult applied = fixture.history.Execute(
            "Change Value",
            NeutralValueHistoryHandler.CreateChange(slot: 1, before: 0, after: 10));

        Assert.True(applied.succeeded);
        Assert.Equal(10, NeutralValueHistoryHandler.GetValue(1));
        Assert.Equal("Change Value", fixture.history.undoName);
        Assert.True(fixture.history.Undo().succeeded);
        Assert.Equal(0, NeutralValueHistoryHandler.GetValue(1));
        Assert.Equal("Change Value", fixture.history.redoName);
        Assert.True(fixture.history.Redo().succeeded);
        Assert.Equal(10, NeutralValueHistoryHandler.GetValue(1));
    }

    [Fact]
    public void FailedTransitionRemainsAvailableForRetry()
    {
        using var fixture = new HistoryFixture();
        Assert.True(fixture.history.Execute(
            "Conditional",
            NeutralValueHistoryHandler.CreateChange(slot: 1, before: 0, after: 1)).succeeded);

        NeutralValueHistoryHandler.Block(slot: 1, EditorHistoryDirection.Undo);
        Assert.False(fixture.history.Undo().succeeded);
        Assert.True(fixture.history.canUndo);
        Assert.Equal("Conditional", fixture.history.undoName);
        Assert.Equal(1, NeutralValueHistoryHandler.GetValue(1));

        NeutralValueHistoryHandler.Unblock(slot: 1, EditorHistoryDirection.Undo);
        Assert.True(fixture.history.Undo().succeeded);
        NeutralValueHistoryHandler.Block(slot: 1, EditorHistoryDirection.Redo);
        Assert.False(fixture.history.Redo().succeeded);
        Assert.True(fixture.history.canRedo);
        Assert.Equal(0, NeutralValueHistoryHandler.GetValue(1));

        NeutralValueHistoryHandler.Unblock(slot: 1, EditorHistoryDirection.Redo);
        Assert.True(fixture.history.Redo().succeeded);
        Assert.Equal(1, NeutralValueHistoryHandler.GetValue(1));
    }

    [Fact]
    public void AdjacentChangesWithTheSameStableMergeKeyCoalesce()
    {
        using var fixture = new HistoryFixture();
        NeutralValueHistoryHandler.SetValue(slot: 1, value: 2);
        fixture.history.RecordApplied(
            "Change Value",
            NeutralValueHistoryHandler.CreateChange(1, 1, 2, mergeKey: "value/1"));
        NeutralValueHistoryHandler.SetValue(slot: 1, value: 3);
        fixture.history.RecordApplied(
            "Change Value",
            NeutralValueHistoryHandler.CreateChange(1, 2, 3, mergeKey: "value/1"));

        Assert.True(fixture.history.Undo().succeeded);
        Assert.Equal(1, NeutralValueHistoryHandler.GetValue(1));
        Assert.False(fixture.history.canUndo);
        Assert.True(fixture.history.Redo().succeeded);
        Assert.Equal(3, NeutralValueHistoryHandler.GetValue(1));
    }

    [Fact]
    public void TransactionCommitsAndRevertsAtomically()
    {
        using var fixture = new HistoryFixture();
        using (EditorHistoryTransaction transaction = fixture.history.BeginTransaction("Batch"))
        {
            Assert.True(fixture.history.Execute(
                "First",
                NeutralValueHistoryHandler.CreateChange(1, 0, 1)).succeeded);
            Assert.True(fixture.history.Execute(
                "Second",
                NeutralValueHistoryHandler.CreateChange(2, 0, 1)).succeeded);
            transaction.Commit();
        }

        Assert.Equal("Batch", fixture.history.undoName);
        Assert.True(fixture.history.Undo().succeeded);
        Assert.Equal(0, NeutralValueHistoryHandler.GetValue(1));
        Assert.Equal(0, NeutralValueHistoryHandler.GetValue(2));
        Assert.True(fixture.history.Redo().succeeded);
        Assert.Equal(1, NeutralValueHistoryHandler.GetValue(1));
        Assert.Equal(1, NeutralValueHistoryHandler.GetValue(2));
    }

    [Fact]
    public void RecordingANewMutationReleasesTheRedoBranch()
    {
        using var fixture = new HistoryFixture();
        Assert.True(fixture.history.Execute(
            "First",
            NeutralValueHistoryHandler.CreateChange(1, 0, 1)).succeeded);
        Assert.True(fixture.history.Undo().succeeded);
        Assert.True(fixture.history.canRedo);

        Assert.True(fixture.history.Execute(
            "Replacement",
            NeutralValueHistoryHandler.CreateChange(1, 0, 2)).succeeded);

        Assert.False(fixture.history.canRedo);
        Assert.Equal(2, NeutralValueHistoryHandler.GetValue(1));
    }

    [Fact]
    public void IsolatedBranchIsReleasedAndRestoresTheEditingStacks()
    {
        using var fixture = new HistoryFixture();
        Assert.True(fixture.history.Execute(
            "Edit Value",
            NeutralValueHistoryHandler.CreateChange(1, 0, 1)).succeeded);
        Assert.True(fixture.history.Undo().succeeded);

        using (fixture.runtime.interactions.BeginHistoryIsolation())
        {
            Assert.False(fixture.history.canUndo);
            Assert.False(fixture.history.canRedo);
            Assert.True(fixture.history.Execute(
                "Runtime Value",
                NeutralValueHistoryHandler.CreateChange(1, 0, 2)).succeeded);
            Assert.Equal("Runtime Value", fixture.history.undoName);
        }

        Assert.False(fixture.history.canUndo);
        Assert.True(fixture.history.canRedo);
        Assert.Equal("Edit Value", fixture.history.redoName);
        Assert.True(fixture.history.Redo().succeeded);
        Assert.Equal(1, NeutralValueHistoryHandler.GetValue(1));
    }

    [Fact]
    public void FailedTransactionRollbackCanBeRetriedWithoutPartialState()
    {
        using var fixture = new HistoryFixture();
        EditorHistoryTransaction transaction = fixture.history.BeginTransaction("Batch");
        Assert.True(fixture.history.Execute(
            "First",
            NeutralValueHistoryHandler.CreateChange(1, 0, 1)).succeeded);
        Assert.True(fixture.history.Execute(
            "Second",
            NeutralValueHistoryHandler.CreateChange(2, 0, 1)).succeeded);
        NeutralValueHistoryHandler.Block(slot: 1, EditorHistoryDirection.Undo);

        EditorHistoryResult firstAttempt = transaction.Rollback();

        Assert.False(firstAttempt.succeeded);
        Assert.True(firstAttempt.statePreserved);
        Assert.Equal(1, NeutralValueHistoryHandler.GetValue(1));
        Assert.Equal(1, NeutralValueHistoryHandler.GetValue(2));
        NeutralValueHistoryHandler.Unblock(slot: 1, EditorHistoryDirection.Undo);
        Assert.True(transaction.Rollback().succeeded);
        Assert.Equal(0, NeutralValueHistoryHandler.GetValue(1));
        Assert.Equal(0, NeutralValueHistoryHandler.GetValue(2));
        Assert.False(fixture.history.canUndo);
    }

    [Fact]
    public void CompensationFailureFaultsHistoryAndRejectsFurtherTransactions()
    {
        using var fixture = new HistoryFixture();
        EditorHistoryTransaction transaction = fixture.history.BeginTransaction("Batch");
        Assert.True(fixture.history.Execute(
            "First",
            NeutralValueHistoryHandler.CreateChange(1, 0, 1)).succeeded);
        Assert.True(fixture.history.Execute(
            "Second",
            NeutralValueHistoryHandler.CreateChange(2, 0, 1)).succeeded);
        NeutralValueHistoryHandler.Block(slot: 1, EditorHistoryDirection.Undo);
        NeutralValueHistoryHandler.Block(slot: 2, EditorHistoryDirection.Redo);

        EditorHistoryResult result = transaction.Rollback();

        Assert.False(result.succeeded);
        Assert.False(result.statePreserved);
        Assert.True(fixture.history.isFaulted);
        Assert.Equal(1, NeutralValueHistoryHandler.GetValue(1));
        Assert.Equal(0, NeutralValueHistoryHandler.GetValue(2));
        Assert.Throws<InvalidOperationException>(() => fixture.history.BeginTransaction("Rejected"));
        transaction.Dispose();
    }

    [Fact]
    public void MissingHandlerCreatesAnExplicitAvailabilityBarrier()
    {
        using var fixture = new HistoryFixture();
        fixture.history.RecordApplied(
            "Unavailable Change",
            new EditorHistoryChange(
                "tests/missing-history-handler",
                EditorHistoryPayload.FromBytes([1, 2, 3])));

        Assert.False(fixture.history.canUndo);
        Assert.Contains("tests/missing-history-handler", fixture.history.undoUnavailableReason);
        Assert.False(fixture.history.Undo().succeeded);
        Assert.Equal("Unavailable Change", fixture.history.undoName);
    }

    [Fact]
    public void LargePayloadUsesTheBoundedSessionDiskStore()
    {
        using var fixture = new HistoryFixture();
        byte[] payload = new byte[128 * 1024];
        BitConverter.GetBytes(1).CopyTo(payload, 0);
        BitConverter.GetBytes(9).CopyTo(payload, sizeof(int));
        BitConverter.GetBytes(32).CopyTo(payload, sizeof(int) * 2);
        NeutralValueHistoryHandler.SetValue(slot: 1, value: 32);
        fixture.history.RecordApplied(
            "Large Neutral Change",
            new EditorHistoryChange(
                NeutralValueHistoryHandler.KIND,
                EditorHistoryPayload.FromBytes(payload)));

        Assert.Equal(0, fixture.history.residentBytes);
        Assert.Equal(payload.LongLength, fixture.history.diskBytes);
        Assert.True(fixture.history.Undo().succeeded);
        Assert.Equal(9, NeutralValueHistoryHandler.GetValue(1));
    }

    [Fact]
    public void NeutralHistorySurvivesATypeCatalogGenerationChange()
    {
        using var fixture = new HistoryFixture();
        NeutralValueHistoryHandler.SetValue(slot: 1, value: 14);
        fixture.history.RecordApplied(
            "Change Neutral Value",
            NeutralValueHistoryHandler.CreateChange(1, 3, 14));

        fixture.types.Rebuild();
        _ = fixture.runtime.panelCount;

        Assert.True(fixture.history.Undo().succeeded);
        Assert.Equal(3, NeutralValueHistoryHandler.GetValue(1));
        Assert.True(fixture.history.Redo().succeeded);
        Assert.Equal(14, NeutralValueHistoryHandler.GetValue(1));
    }

    private sealed class HistoryFixture : IDisposable
    {
        private readonly string m_projectRoot = Path.Combine(
            Path.GetTempPath(),
            "InnoEditorHistoryTests",
            Guid.NewGuid().ToString("N"));
        private readonly ModuleHost m_modules;
        private readonly LogRouter m_logs = new();

        internal HistoryFixture()
        {
            Directory.CreateDirectory(Path.Combine(m_projectRoot, "Assets"));
            NeutralValueHistoryHandler.Reset();
            m_modules = new ModuleHost(new ModuleHostOptions
            {
                cacheDirectory = Path.Combine(m_projectRoot, "Library", "Assemblies")
            });
            types = new TypeCatalog(m_modules);
            runtime = new EditorInteractionRuntime(
                new EditorContext(m_projectRoot),
                types,
                m_logs,
                [types]);
            runtime.Start();
        }

        internal TypeCatalog types { get; }

        internal EditorInteractionRuntime runtime { get; }

        internal IEditorHistory history => runtime.interactions.history;

        public void Dispose()
        {
            runtime.Dispose();
            types.Dispose();
            m_modules.Dispose();
            m_logs.Dispose();
            if (Directory.Exists(m_projectRoot))
                Directory.Delete(m_projectRoot, recursive: true);
        }
    }
}

[EditorHistoryHandler(NeutralValueHistoryHandler.KIND)]
public sealed class NeutralValueHistoryHandler : EditorHistoryHandler
{
    public const string KIND = "tests/neutral-values";

    private static readonly HashSet<(int Slot, EditorHistoryDirection Direction)> S_BLOCKED = [];
    private static readonly Dictionary<int, int> S_VALUES = [];

    public static EditorHistoryChange CreateChange(
        int slot,
        int before,
        int after,
        string? mergeKey = null)
    {
        byte[] bytes = new byte[sizeof(int) * 3];
        BitConverter.GetBytes(slot).CopyTo(bytes, 0);
        BitConverter.GetBytes(before).CopyTo(bytes, sizeof(int));
        BitConverter.GetBytes(after).CopyTo(bytes, sizeof(int) * 2);
        return new EditorHistoryChange(KIND, EditorHistoryPayload.FromBytes(bytes), mergeKey);
    }

    public static int GetValue(int slot)
        => S_VALUES.TryGetValue(slot, out int value) ? value : 0;

    public static void SetValue(int slot, int value)
        => S_VALUES[slot] = value;

    public static void Block(int slot, EditorHistoryDirection direction)
        => S_BLOCKED.Add((slot, direction));

    public static void Unblock(int slot, EditorHistoryDirection direction)
        => S_BLOCKED.Remove((slot, direction));

    public static void Reset()
    {
        S_BLOCKED.Clear();
        S_VALUES.Clear();
    }

    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
        => change.payload.length >= sizeof(int) * 3
            ? EditorHistoryAvailability.Available()
            : EditorHistoryAvailability.Unavailable("The neutral value payload is truncated.");

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        byte[] bytes = change.payload.ReadBytes();
        int slot = BitConverter.ToInt32(bytes, 0);
        if (S_BLOCKED.Contains((slot, direction)))
            return EditorHistoryResult.Failure($"Slot {slot} is blocked for {direction}.");
        int offset = direction == EditorHistoryDirection.Undo
            ? sizeof(int)
            : sizeof(int) * 2;
        S_VALUES[slot] = BitConverter.ToInt32(bytes, offset);
        return EditorHistoryResult.Success();
    }

    protected override bool TryMerge(
        EditorHistoryChange older,
        EditorHistoryChange newer,
        out EditorHistoryChange? merged)
    {
        byte[] olderBytes = older.payload.ReadBytes();
        byte[] newerBytes = newer.payload.ReadBytes();
        int slot = BitConverter.ToInt32(olderBytes, 0);
        if (slot != BitConverter.ToInt32(newerBytes, 0))
        {
            merged = null;
            return false;
        }

        merged = CreateChange(
            slot,
            BitConverter.ToInt32(olderBytes, sizeof(int)),
            BitConverter.ToInt32(newerBytes, sizeof(int) * 2),
            older.mergeKey);
        return true;
    }
}
