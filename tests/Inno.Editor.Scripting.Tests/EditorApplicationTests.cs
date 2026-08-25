using System;
using System.Collections.Generic;

using Inno.Editor.Application;

using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class EditorApplicationTests
{
    [Theory]
    [InlineData()]
    [InlineData("one", "two")]
    public void ProjectDirectoryParserRejectsEveryArgumentCountExceptOne(params string[] args)
    {
        Assert.False(Program.TryGetProjectDirectory(args, out string? projectDirectory));
        Assert.Null(projectDirectory);
    }

    [Fact]
    public void ProjectDirectoryParserReturnsTheSingleArgument()
    {
        Assert.True(Program.TryGetProjectDirectory(["Project"], out string? projectDirectory));
        Assert.Equal("Project", projectDirectory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void StagedResourceFailureReleasesAcquiredStagesOnceInReverseOrder(int failingStage)
    {
        var cleanup = new List<int>();
        var failures = new List<Exception>();
        var stack = new EditorHostResourceStack(failures.Add);

        Assert.Throws<StageFailureException>(() => AcquireStages(stack, failingStage, cleanup));
        stack.Dispose();
        stack.Dispose();

        Assert.Equal(ReverseRange(failingStage), cleanup);
        Assert.Empty(failures);
    }

    [Fact]
    public void StagedResourceCleanupContinuesAfterFailureAndReportsIt()
    {
        var cleanup = new List<int>();
        var failures = new List<Exception>();
        var stack = new EditorHostResourceStack(failures.Add);
        _ = stack.Acquire(static () => 0, value => cleanup.Add(value));
        _ = stack.Acquire(static () => 1, value =>
        {
            cleanup.Add(value);
            throw new InvalidOperationException("cleanup");
        });
        _ = stack.Acquire(static () => 2, value => cleanup.Add(value));

        stack.Dispose();

        Assert.Equal([2, 1, 0], cleanup);
        Assert.Single(failures);
        Assert.Equal("cleanup", failures[0].Message);
    }

    private static void AcquireStages(
        EditorHostResourceStack stack,
        int failingStage,
        ICollection<int> cleanup)
    {
        for (int stage = 0; stage < 5; stage++)
        {
            int current = stage;
            _ = stack.Acquire(
                () => current == failingStage ? throw new StageFailureException() : current,
                cleanup.Add);
        }
    }

    private static int[] ReverseRange(int count)
    {
        var result = new int[count];
        for (int i = 0; i < count; i++)
            result[i] = count - i - 1;
        return result;
    }

    private sealed class StageFailureException : Exception;
}
