using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

using Inno.Editor.Application;

using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class EditorApplicationTests
{
    private static readonly Assembly S_APPLICATION_ASSEMBLY = Assembly.Load("Inno.Editor.Application");

    [Theory]
    [InlineData()]
    [InlineData("one", "two")]
    public void ProjectDirectoryParserRejectsEveryArgumentCountExceptOne(params string[] args)
    {
        Assert.False(TryGetProjectDirectory(args, out string? projectDirectory));
        Assert.Null(projectDirectory);
    }

    [Fact]
    public void ProjectDirectoryParserReturnsTheSingleArgument()
    {
        Assert.True(TryGetProjectDirectory(["Project"], out string? projectDirectory));
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
        var stack = new ReflectedHostResourceStack(failures.Add);

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
        var stack = new ReflectedHostResourceStack(failures.Add);
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
        ReflectedHostResourceStack stack,
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

    private static bool TryGetProjectDirectory(string[] args, out string? projectDirectory)
    {
        Type programType = S_APPLICATION_ASSEMBLY.GetType(
            "Inno.Editor.Application.Program",
            throwOnError: true)!;
        MethodInfo method = programType.GetMethod(
            "TryGetProjectDirectory",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        object?[] arguments = [args, null];
        bool result = (bool)method.Invoke(null, arguments)!;
        projectDirectory = (string?)arguments[1];
        return result;
    }

    private sealed class StageFailureException : Exception;

    private sealed class ReflectedHostResourceStack : IDisposable
    {
        private static readonly Type S_STACK_TYPE = S_APPLICATION_ASSEMBLY.GetType(
            "Inno.Editor.Application.EditorHostResourceStack",
            throwOnError: true)!;
        private readonly object m_instance;

        internal ReflectedHostResourceStack(Action<Exception> reportCleanupFailure)
        {
            m_instance = Activator.CreateInstance(
                S_STACK_TYPE,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: [reportCleanupFailure],
                culture: null)!;
        }

        internal T Acquire<T>(Func<T> factory, Action<T> cleanup)
        {
            MethodInfo method = S_STACK_TYPE
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(candidate => candidate.Name == "Acquire" && candidate.IsGenericMethodDefinition)
                .MakeGenericMethod(typeof(T));
            try
            {
                return (T)method.Invoke(m_instance, [factory, cleanup])!;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        public void Dispose() => ((IDisposable)m_instance).Dispose();
    }
}
