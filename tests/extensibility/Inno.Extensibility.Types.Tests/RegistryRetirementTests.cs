using System;
using System.Collections.Generic;
using System.IO;

using Inno.Core.Execution;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Reload;
using Inno.Extensibility.Types;

using Xunit;

namespace Inno.Extensibility.Types.Tests;

[Collection(TypeCacheCollection.NAME)]
public sealed class RegistryRetirementTests
{
    [Fact]
    public void PendingBatchDoesNotRepeatCompletedResourcesOrReleaseLowerDependenciesEarly()
    {
        string cache = Path.Combine(Path.GetTempPath(), "InnoRegistryRetirement", Guid.NewGuid().ToString("N"));
        using var modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = cache });
        using var types = new TypeCatalog(modules);
        var calls = new List<string>();
        int pendingAttempts = 0;
        var first = new Resource(() => calls.Add("first"));
        var second = new Resource(() =>
        {
            calls.Add("second");
            if (++pendingAttempts < 3)
                throw new RetirementPendingException("Pending registry resource.");
        });
        var third = new Resource(() =>
        {
            calls.Add("third");
            throw new InvalidOperationException("Earlier completed resource failure.");
        });
        using var registry = new Registry(types, () => [first, second, third, first]);
        registry.Refresh();

        AggregateException failure = Assert.Throws<AggregateException>(registry.Dispose);
        Assert.Contains("Earlier completed resource failure", failure.ToString());
        Assert.Equal(new[] { "third", "second", "second", "second", "first" }, calls);
        Assert.Equal(GenerationState.Faulted, modules.generations.state);
        registry.Dispose();
        Assert.Equal(5, calls.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeadlineFailureRetainsDependenciesAndPermanentlyRejectsFurtherRetirement(bool clear)
    {
        string cache = Path.Combine(Path.GetTempPath(), "InnoRegistryRetirement", Guid.NewGuid().ToString("N"));
        var modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = cache });
        var types = new TypeCatalog(modules);
        int released = 0;
        int attempts = 0;
        var registry = new Registry(types, () =>
        [
            new Resource(() => released++),
            new Resource(() =>
            {
                attempts++;
                throw new RetirementPendingException("Unfinished native callback.");
            })
        ], TimeSpan.FromMilliseconds(5));
        registry.Refresh();

        RetirementTimeoutException failure = Assert.Throws<RetirementTimeoutException>(clear ? registry.Clear : registry.Dispose);
        int attemptsAtFailure = attempts;
        Assert.Equal(0, released);
        Assert.Same(failure, Assert.Throws<RetirementTimeoutException>(registry.Dispose));
        Assert.Same(failure, Assert.Throws<RetirementTimeoutException>(registry.Refresh));
        Assert.Equal(attemptsAtFailure, attempts);
        Assert.Equal(GenerationState.Faulted, modules.generations.state);
        Assert.ThrowsAny<InvalidOperationException>(() => modules.generations.EnsureReady("reload"));
        Assert.Throws<RetirementTimeoutException>(types.Dispose);
        Assert.Throws<RetirementTimeoutException>(types.Dispose);
        Assert.Throws<RetirementTimeoutException>(modules.Dispose);
        Assert.Throws<RetirementTimeoutException>(modules.Dispose);
    }

    private sealed class Registry(TypeCatalog types, Func<object[]> factory, TimeSpan? timeout = null)
        : TypeRegistry<object[]>(types, timeout)
    {
        protected override object[] Build(TypeCacheSnapshot types) => factory();

        protected override void DisposeSnapshot(object[] snapshot) => DisposeExtensions(snapshot);
    }

    private sealed class Resource(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
