using System;
using System.Collections.Generic;
using System.IO;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Xunit;

namespace Inno.Editor.Interactions.Tests;

public sealed class EditorHistoryTests
{
    [Fact]
    public void ExecuteUndoAndRedoMoveOperationBetweenStacks()
    {
        using var history = new EditorHistory();
        int value = 0;

        EditorHistoryResult applied = history.Execute(
            "Change Value",
            () =>
            {
                value = 10;
                return EditorHistoryResult.Success();
            },
            () =>
            {
                value = 0;
                return EditorHistoryResult.Success();
            });

        Assert.True(applied.succeeded);
        Assert.Equal(10, value);
        Assert.Equal("Change Value", history.undoName);
        Assert.True(history.Undo().succeeded);
        Assert.Equal(0, value);
        Assert.Equal("Change Value", history.redoName);
        Assert.True(history.Redo().succeeded);
        Assert.Equal(10, value);
    }

    [Fact]
    public void FailedRedoRemainsAvailableForRetry()
    {
        using var history = new EditorHistory();
        bool allowRedo = true;
        int value = 0;
        Assert.True(history.Execute(
            "Conditional",
            () =>
            {
                if (!allowRedo)
                    return EditorHistoryResult.Failure("Blocked");
                value = 1;
                return EditorHistoryResult.Success();
            },
            () =>
            {
                value = 0;
                return EditorHistoryResult.Success();
            }).succeeded);
        Assert.True(history.Undo().succeeded);

        allowRedo = false;
        Assert.False(history.Redo().succeeded);
        Assert.True(history.canRedo);
        Assert.Equal(0, value);

        allowRedo = true;
        Assert.True(history.Redo().succeeded);
        Assert.Equal(1, value);
    }

    [Fact]
    public void AdjacentValueEditsWithSameKeyCoalesce()
    {
        using var history = new EditorHistory();
        int value = 1;
        object key = new();

        value = 2;
        history.RecordValue("Change Value", 1, 2, updated => value = updated, key);
        value = 3;
        history.RecordValue("Change Value", 2, 3, updated => value = updated, key);

        Assert.True(history.Undo().succeeded);
        Assert.Equal(1, value);
        Assert.False(history.canUndo);
        Assert.True(history.Redo().succeeded);
        Assert.Equal(3, value);
    }

    [Fact]
    public void TransactionCommitsAndRevertsAtomically()
    {
        using var history = new EditorHistory();
        int first = 0;
        int second = 0;
        using (EditorHistoryTransaction transaction = history.BeginTransaction("Batch"))
        {
            Assert.True(history.Execute(
                "First",
                () =>
                {
                    first = 1;
                    return EditorHistoryResult.Success();
                },
                () =>
                {
                    first = 0;
                    return EditorHistoryResult.Success();
                }).succeeded);
            Assert.True(history.Execute(
                "Second",
                () =>
                {
                    second = 1;
                    return EditorHistoryResult.Success();
                },
                () =>
                {
                    second = 0;
                    return EditorHistoryResult.Success();
                }).succeeded);
            transaction.Commit();
        }

        Assert.Equal("Batch", history.undoName);
        Assert.True(history.Undo().succeeded);
        Assert.Equal(0, first);
        Assert.Equal(0, second);
        Assert.True(history.Redo().succeeded);
        Assert.Equal(1, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public void RecordingANewMutationDisposesTheRedoBranch()
    {
        using var history = new EditorHistory();
        int value = 0;
        Assert.True(history.Execute(
            "First",
            () =>
            {
                value = 1;
                return EditorHistoryResult.Success();
            },
            () =>
            {
                value = 0;
                return EditorHistoryResult.Success();
            }).succeeded);
        Assert.True(history.Undo().succeeded);
        Assert.True(history.canRedo);

        Assert.True(history.Execute(
            "Replacement",
            () =>
            {
                value = 2;
                return EditorHistoryResult.Success();
            },
            () =>
            {
                value = 0;
                return EditorHistoryResult.Success();
            }).succeeded);

        Assert.False(history.canRedo);
        Assert.Equal(2, value);
    }

    [Fact]
    public void FailedTransactionRollbackRemainsOnStackAndCanBeRetried()
    {
        using var history = new EditorHistory();
        int first = 0;
        int second = 0;
        bool allowFirstUndo = false;
        EditorHistoryTransaction transaction = history.BeginTransaction("Batch");
        Assert.True(history.Execute(
            "First",
            () =>
            {
                first = 1;
                return EditorHistoryResult.Success();
            },
            () =>
            {
                if (!allowFirstUndo)
                    return EditorHistoryResult.Failure("First is busy.");
                first = 0;
                return EditorHistoryResult.Success();
            }).succeeded);
        Assert.True(history.Execute(
            "Second",
            () =>
            {
                second = 1;
                return EditorHistoryResult.Success();
            },
            () =>
            {
                second = 0;
                return EditorHistoryResult.Success();
            }).succeeded);

        EditorHistoryResult firstAttempt = transaction.Rollback();

        Assert.False(firstAttempt.succeeded);
        Assert.True(firstAttempt.statePreserved);
        Assert.Equal(1, first);
        Assert.Equal(1, second);
        allowFirstUndo = true;
        Assert.True(transaction.Rollback().succeeded);
        Assert.Equal(0, first);
        Assert.Equal(0, second);
        Assert.False(history.canUndo);
    }

    [Fact]
    public void DisposeCommitsAppliedTransactionWhenRollbackFailsSafely()
    {
        using var history = new EditorHistory();
        int value = 0;
        bool allowUndo = false;
        EditorHistoryTransaction transaction = history.BeginTransaction("Batch");
        Assert.True(history.Execute(
            "Value",
            () =>
            {
                value = 1;
                return EditorHistoryResult.Success();
            },
            () =>
            {
                if (!allowUndo)
                    return EditorHistoryResult.Failure("Value is busy.");
                value = 0;
                return EditorHistoryResult.Success();
            }).succeeded);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(transaction.Dispose);

        Assert.Contains("committed to Undo", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, value);
        Assert.Equal("Batch", history.undoName);
        allowUndo = true;
        Assert.True(history.Undo().succeeded);
        Assert.Equal(0, value);
    }

    [Fact]
    public void CompensationFailureFaultsHistoryAndPreventsFurtherTransitions()
    {
        using var history = new EditorHistory();
        int first = 0;
        int second = 0;
        bool initialSecondApply = true;
        EditorHistoryTransaction transaction = history.BeginTransaction("Batch");
        Assert.True(history.Execute(
            "First",
            () =>
            {
                first = 1;
                return EditorHistoryResult.Success();
            },
            () => EditorHistoryResult.Failure("First cannot undo.")).succeeded);
        Assert.True(history.Execute(
            "Second",
            () =>
            {
                if (!initialSecondApply)
                    return EditorHistoryResult.Failure("Second cannot compensate.");
                initialSecondApply = false;
                second = 1;
                return EditorHistoryResult.Success();
            },
            () =>
            {
                second = 0;
                return EditorHistoryResult.Success();
            }).succeeded);

        EditorHistoryResult result = transaction.Rollback();

        Assert.False(result.succeeded);
        Assert.False(result.statePreserved);
        Assert.True(history.isFaulted);
        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Throws<InvalidOperationException>(() => history.BeginTransaction("Rejected"));
        transaction.Dispose();
    }

    [Fact]
    public void FaultedTransactionRetainsChildrenUntilHistoryIsCleared()
    {
        using var history = new EditorHistory();
        var first = new TrackingHistoryOperation(
            "First",
            undo: () => EditorHistoryResult.Failure("First cannot undo."),
            redo: EditorHistoryResult.Success);
        var second = new TrackingHistoryOperation(
            "Second",
            undo: EditorHistoryResult.Success,
            redo: () => EditorHistoryResult.Failure("Second cannot compensate."));
        EditorHistoryTransaction transaction = history.BeginTransaction("Batch");
        history.RecordApplied(first);
        history.RecordApplied(second);

        EditorHistoryResult result = transaction.Rollback();

        Assert.False(result.statePreserved);
        Assert.False(first.isDisposed);
        Assert.False(second.isDisposed);
        history.Clear();
        Assert.True(first.isDisposed);
        Assert.True(second.isDisposed);
    }

    [Fact]
    public void FailedUndoDoesNotMoveTheOperationBetweenStacks()
    {
        using var history = new EditorHistory();
        bool allowUndo = false;
        int value = 0;
        Assert.True(history.Execute(
            "Conditional",
            () =>
            {
                value = 1;
                return EditorHistoryResult.Success();
            },
            () =>
            {
                if (!allowUndo)
                    return EditorHistoryResult.Failure("Blocked");
                value = 0;
                return EditorHistoryResult.Success();
            }).succeeded);

        Assert.False(history.Undo().succeeded);
        Assert.Equal("Conditional", history.undoName);
        Assert.Null(history.redoName);
        Assert.Equal(1, value);

        allowUndo = true;
        Assert.True(history.Undo().succeeded);
        Assert.Equal(0, value);
    }

    [Fact]
    public void HandlerMapRollbackRestoresThePreviousGenerationWithoutDiscardingHistory()
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "InnoHistoryHandlerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectRoot);
        var context = new EditorContext(projectRoot);
        var interactions = new EditorInteractions(context);
        using var history = new EditorHistory();
        history.Attach(context, interactions);
        var handler = new TestNeutralHistoryHandler();
        EditorHistory.HandlerUpdate initial = history.PrepareHandlerUpdate(
            new Dictionary<string, EditorHistoryHandler>(StringComparer.Ordinal)
            {
                [TestNeutralHistoryHandler.C_KIND] = handler
            });
        initial.Activate();
        initial.Complete();
        TestNeutralHistoryHandler.value = 1;
        history.RecordApplied(
            "Neutral",
            new EditorHistoryChange(
                TestNeutralHistoryHandler.C_KIND,
                EditorHistoryPayload.FromBytes([])));
        EditorHistory.HandlerUpdate candidate = history.PrepareHandlerUpdate(
            new Dictionary<string, EditorHistoryHandler>(StringComparer.Ordinal));
        candidate.Activate();
        candidate.Rollback();

        Assert.True(history.Undo().succeeded);
        Assert.Equal(0, TestNeutralHistoryHandler.value);

        interactions.historyHost.Dispose();
        Directory.Delete(projectRoot, recursive: true);
    }
}

internal sealed class TestNeutralHistoryHandler : EditorHistoryHandler
{
    internal const string C_KIND = "tests.neutral-handler-map";
    internal static int value;

    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
        => EditorHistoryAvailability.Available();

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        value = direction == EditorHistoryDirection.Undo ? 0 : 1;
        return EditorHistoryResult.Success();
    }
}

internal sealed class TrackingHistoryOperation(
    string operationName,
    Func<EditorHistoryResult> undo,
    Func<EditorHistoryResult> redo) : EditorHistoryOperation
{
    internal bool isDisposed { get; private set; }

    public override string name => operationName;

    protected override EditorHistoryResult Undo() => undo();

    protected override EditorHistoryResult Redo() => redo();

    protected override void Dispose(bool disposing) => isDisposed = disposing;
}
