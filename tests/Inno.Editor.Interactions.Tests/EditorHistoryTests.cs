using System;

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
}
