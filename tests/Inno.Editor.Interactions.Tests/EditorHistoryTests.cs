using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Xunit;

namespace Inno.Editor.Interactions.Tests;

public sealed class EditorHistoryTests
{
    [Fact]
    public void ExecuteUndoAndRedoMoveOperationBetweenStacks()
    {
        using var history = new ReflectedEditorHistory();
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
        using var history = new ReflectedEditorHistory();
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
        using var history = new ReflectedEditorHistory();
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
        using var history = new ReflectedEditorHistory();
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
        using var history = new ReflectedEditorHistory();
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
        using var history = new ReflectedEditorHistory();
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
        using var history = new ReflectedEditorHistory();
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
        using var history = new ReflectedEditorHistory();
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
        using var history = new ReflectedEditorHistory();
        int secondRedoCount = 0;
        EditorHistoryTransaction transaction = history.BeginTransaction("Batch");
        Assert.True(history.Execute(
            "First",
            EditorHistoryResult.Success,
            () => EditorHistoryResult.Failure("First cannot undo.")).succeeded);
        Assert.True(history.Execute(
            "Second",
            () => ++secondRedoCount == 1
                ? EditorHistoryResult.Success()
                : EditorHistoryResult.Failure("Second cannot compensate."),
            EditorHistoryResult.Success).succeeded);
        object[] children = GetActiveTransactionChildren(history);

        EditorHistoryResult result = transaction.Rollback();

        Assert.False(result.statePreserved);
        Assert.All(children, child => Assert.False(IsHistoryOperationDisposed(child)));
        history.Clear();
        Assert.All(children, child => Assert.True(IsHistoryOperationDisposed(child)));
    }

    [Fact]
    public void FailedUndoDoesNotMoveTheOperationBetweenStacks()
    {
        using var history = new ReflectedEditorHistory();
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
        var interactions = (EditorInteractions)EditorTestReflection.Create(
            typeof(EditorInteractions).Assembly,
            "Inno.Editor.Interactions.EditorInteractions",
            context);
        using var history = new ReflectedEditorHistory();
        history.Attach(context, interactions);
        var handler = new TestNeutralHistoryHandler();
        ReflectedHistoryUpdate initial = history.PrepareHandlerUpdate(
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
        ReflectedHistoryUpdate candidate = history.PrepareHandlerUpdate(
            new Dictionary<string, EditorHistoryHandler>(StringComparer.Ordinal));
        candidate.Activate();
        candidate.Rollback();

        Assert.True(history.Undo().succeeded);
        Assert.Equal(0, TestNeutralHistoryHandler.value);

        PropertyInfo historyHostProperty = typeof(EditorInteractions).GetProperty(
            "historyHost",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((IDisposable)historyHostProperty.GetValue(interactions)!).Dispose();
        Directory.Delete(projectRoot, recursive: true);
    }

    private static object[] GetActiveTransactionChildren(ReflectedEditorHistory history)
    {
        FieldInfo transactionsField = history.instance.GetType().GetField(
            "m_transactions",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var transactions = (IEnumerable)transactionsField.GetValue(history.instance)!;
        object transaction = Assert.Single(transactions.Cast<object>());
        FieldInfo childrenField = transaction.GetType().GetField(
            "m_children",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((IEnumerable)childrenField.GetValue(transaction)!).Cast<object>().ToArray();
    }

    private static bool IsHistoryOperationDisposed(object operation)
    {
        Type operationType = typeof(EditorInteractions).Assembly.GetType(
            "Inno.Editor.Interactions.EditorHistoryOperation",
            throwOnError: true)!;
        FieldInfo disposedField = operationType.GetField(
            "m_isDisposed",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (bool)disposedField.GetValue(operation)!;
    }
}

internal sealed class ReflectedEditorHistory : IDisposable
{
    private static readonly Type S_HISTORY_TYPE = typeof(EditorInteractions).Assembly.GetType(
        "Inno.Editor.Interactions.EditorHistory",
        throwOnError: true)!;

    internal ReflectedEditorHistory()
    {
        instance = Activator.CreateInstance(
            S_HISTORY_TYPE,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [256],
            culture: null)!;
    }

    internal object instance { get; }

    internal bool canUndo => GetProperty<bool>("canUndo");

    internal bool canRedo => GetProperty<bool>("canRedo");

    internal bool isFaulted => GetProperty<bool>("isFaulted");

    internal string? undoName => GetProperty<string?>("undoName");

    internal string? redoName => GetProperty<string?>("redoName");

    internal EditorHistoryTransaction BeginTransaction(string name)
        => Invoke<EditorHistoryTransaction>(FindMethod("BeginTransaction", parameterCount: 1), name);

    internal EditorHistoryResult Execute(
        string name,
        Func<EditorHistoryResult> execute,
        Func<EditorHistoryResult> undo)
        => Invoke<EditorHistoryResult>(FindMethod("Execute", parameterCount: 4), name, execute, undo, null);

    internal void RecordValue<T>(string name, T before, T after, Action<T> apply, object mergeKey)
    {
        MethodInfo method = FindMethod("RecordValue", parameterCount: 5).MakeGenericMethod(typeof(T));
        _ = Invoke<object?>(method, name, before, after, apply, mergeKey);
    }

    internal void RecordApplied(string name, EditorHistoryChange change)
        => _ = Invoke<object?>(FindMethod("RecordApplied", parameterCount: 2), name, change);

    internal EditorHistoryResult Undo()
        => Invoke<EditorHistoryResult>(FindMethod("Undo", parameterCount: 0));

    internal EditorHistoryResult Redo()
        => Invoke<EditorHistoryResult>(FindMethod("Redo", parameterCount: 0));

    internal void Clear()
        => _ = Invoke<object?>(FindMethod("Clear", parameterCount: 0));

    internal void Attach(EditorContext context, EditorInteractions interactions)
        => _ = Invoke<object?>(FindMethod("Attach", parameterCount: 2), context, interactions);

    internal ReflectedHistoryUpdate PrepareHandlerUpdate(
        IReadOnlyDictionary<string, EditorHistoryHandler> handlers)
        => new(Invoke<object>(FindMethod("PrepareHandlerUpdate", parameterCount: 1), handlers));

    public void Dispose() => ((IDisposable)instance).Dispose();

    private T GetProperty<T>(string name)
        => (T)S_HISTORY_TYPE.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static MethodInfo FindMethod(string name, int parameterCount)
        => S_HISTORY_TYPE.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(method => method.Name == name && method.GetParameters().Length == parameterCount);

    private T Invoke<T>(MethodInfo method, params object?[]? arguments)
    {
        try
        {
            return (T)method.Invoke(instance, arguments)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}

internal sealed class ReflectedHistoryUpdate(object instance)
{
    internal void Activate() => Invoke("Activate");

    internal void Rollback() => Invoke("Rollback");

    internal void Complete() => Invoke("Complete");

    private void Invoke(string name)
    {
        try
        {
            _ = instance.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(
                    instance,
                    parameters: null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
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
