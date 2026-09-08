using System;
using System.IO;
using System.Linq;
using System.Runtime.Loader;

using Inno.Core.Execution;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Reload;
using Inno.Extensibility.Types;

using Xunit;

namespace Inno.Extensibility.Modules.Tests;

public sealed class ModuleRetirementTests
{
    [Theory]
    [InlineData("activate")]
    [InlineData("complete")]
    [InlineData("rollback")]
    public void PendingParticipantDoesNotBeginUnloadingEitherLiveGeneration(string phase)
    {
        string cache = Path.Combine(Path.GetTempPath(), "InnoModuleRetirement", Guid.NewGuid().ToString("N"));
        var modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = cache });
        var types = new TypeCatalog(modules);
        AssemblyModuleHandle handle = modules.Load(Request("V1"));
        var participant = new Participant();
        using IDisposable registration = modules.RegisterCatalogParticipant(participant);
        participant.phase = phase;
        var reload = modules.BeginReload(handle, Request("V2"));
        AssemblyLoadContext previous = Context(reload.context.previousCatalog);
        AssemblyLoadContext candidate = Context(reload.context.candidateCatalog);
        int unloads = 0;
        previous.Unloading += _ => unloads++;
        candidate.Unloading += _ => unloads++;

        if (phase == "activate")
            Assert.Throws<RetirementTimeoutException>(reload.Activate);
        else
        {
            reload.Activate();
            if (phase == "complete")
                Assert.Throws<RetirementTimeoutException>(() => reload.Complete());
            else
                Assert.Throws<RetirementTimeoutException>(reload.Rollback);
        }

        Assert.Equal(GenerationState.Faulted, modules.generations.state);
        Assert.Equal(0, unloads);
        Assert.NotEmpty(reload.context.previousCatalog.assemblies);
        Assert.NotEmpty(reload.context.candidateCatalog.assemblies);
        Assert.Throws<RetirementTimeoutException>(reload.Dispose);
        Assert.Throws<RetirementTimeoutException>(reload.Dispose);
        Assert.Throws<RetirementTimeoutException>(types.Dispose);
        Assert.Throws<RetirementTimeoutException>(modules.Dispose);
        Assert.Throws<RetirementTimeoutException>(modules.Dispose);
        Assert.Equal(0, unloads);
    }

    [Theory]
    [InlineData("activate")]
    [InlineData("complete")]
    [InlineData("rollback")]
    public void WrappedPendingKeepsBothCollectibleContextsAndPreservesTheOriginalFailure(string phase)
    {
        string cache = Path.Combine(Path.GetTempPath(), "InnoWrappedRetirement", Guid.NewGuid().ToString("N"));
        var modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = cache });
        var types = new TypeCatalog(modules);
        AssemblyModuleHandle handle = modules.Load(Request("V1"));
        var participant = new Participant();
        using IDisposable registration = modules.RegisterCatalogParticipant(participant);
        participant.phase = phase;
        participant.failure = new AggregateException(new InvalidOperationException("completed sibling"),
            new InvalidOperationException("native owner", new RetirementTimeoutException("callback still active")));
        var reload = modules.BeginReload(handle, Request("V2"));
        int unloads = 0;
        Context(reload.context.previousCatalog).Unloading += _ => unloads++;
        Context(reload.context.candidateCatalog).Unloading += _ => unloads++;
        if (phase == "activate")
            Assert.Same(participant.failure, Assert.Throws<AggregateException>(reload.Activate));
        else
        {
            reload.Activate();
            Assert.Same(participant.failure, Assert.Throws<AggregateException>(() =>
            {
                if (phase == "complete") _ = reload.Complete();
                else reload.Rollback();
            }));
        }
        Assert.Equal(GenerationState.Faulted, modules.generations.state);
        Assert.Same(participant.failure, Assert.Throws<AggregateException>(reload.Dispose));
        Assert.NotNull(RetirementPendingException.Find(Assert.ThrowsAny<Exception>(types.Dispose)));
        Assert.NotNull(RetirementPendingException.Find(Assert.ThrowsAny<Exception>(modules.Dispose)));
        Assert.Equal(0, unloads);
        Assert.NotEmpty(reload.context.previousCatalog.assemblies);
        Assert.NotEmpty(reload.context.candidateCatalog.assemblies);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("rollback")]
    public void CatalogParticipantErrorsBeforePendingDoNotDisappearOrUnloadEitherContext(string phase)
    {
        string cache = Path.Combine(Path.GetTempPath(), "InnoCatalogRetirement", Guid.NewGuid().ToString("N"));
        var modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = cache });
        var types = new TypeCatalog(modules);
        AssemblyModuleHandle handle = modules.Load(Request("V1"));
        var ordinary = new Participant { failure = new InvalidOperationException("earlier catalog cleanup") };
        var pending = new Participant();
        using IDisposable first = modules.RegisterCatalogParticipant(phase == "complete" ? ordinary : pending);
        using IDisposable second = modules.RegisterCatalogParticipant(phase == "complete" ? pending : ordinary);
        ordinary.phase = phase;
        pending.phase = phase;
        var reload = modules.BeginReload(handle, Request("V2"));
        int unloads = 0;
        Context(reload.context.previousCatalog).Unloading += _ => unloads++;
        Context(reload.context.candidateCatalog).Unloading += _ => unloads++;
        reload.Activate();
        AggregateException failure = Assert.Throws<AggregateException>(() =>
        {
            if (phase == "complete") _ = reload.Complete();
            else reload.Rollback();
        });
        Assert.Contains(ordinary.failure, failure.Flatten().InnerExceptions);
        Assert.Same(pending.failure, RetirementPendingException.Find(failure));
        Assert.Same(failure, Assert.Throws<AggregateException>(reload.Dispose));
        Assert.Equal(GenerationState.Faulted, modules.generations.state);
        Assert.Equal(0, unloads);
        Assert.NotNull(RetirementPendingException.Find(Assert.ThrowsAny<Exception>(types.Dispose)));
        Assert.NotNull(RetirementPendingException.Find(Assert.ThrowsAny<Exception>(modules.Dispose)));
    }

    [Fact]
    public void RejectedCatalogActivationRemainsVisibleWhenItsRollbackIsPending()
    {
        string cache = Path.Combine(Path.GetTempPath(), "InnoRejectedRetirement", Guid.NewGuid().ToString("N"));
        var modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = cache });
        var types = new TypeCatalog(modules);
        AssemblyModuleHandle handle = modules.Load(Request("V1"));
        var participant = new Participant();
        using IDisposable registration = modules.RegisterCatalogParticipant(participant);
        participant.phase = "rollback";
        participant.activationFailure = new InvalidOperationException("catalog activation rejected");
        var reload = modules.BeginReload(handle, Request("V2"));
        int unloads = 0;
        Context(reload.context.previousCatalog).Unloading += _ => unloads++;
        Context(reload.context.candidateCatalog).Unloading += _ => unloads++;
        AggregateException failure = Assert.Throws<AggregateException>(reload.Activate);
        Assert.Contains(participant.activationFailure, failure.Flatten().InnerExceptions);
        Assert.Same(participant.failure, RetirementPendingException.Find(failure));
        Assert.Same(failure, Assert.Throws<AggregateException>(reload.Dispose));
        Assert.Equal(0, unloads);
        Assert.NotNull(RetirementPendingException.Find(Assert.ThrowsAny<Exception>(types.Dispose)));
        Assert.NotNull(RetirementPendingException.Find(Assert.ThrowsAny<Exception>(modules.Dispose)));
    }

    private static AssemblyLoadRequest Request(string variant)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Modules", variant);
        return new AssemblyLoadRequest
        {
            moduleName = "RetirementTests",
            mainAssemblyPath = Path.Combine(directory, "Inno.Extensibility.Modules.TestModule.dll"),
            preloadAssemblyPaths = [Path.Combine(directory, "Reloadable.PrivateDependency.dll")],
            domain = AssemblyDomain.InnoPlugin,
            scope = AssemblyScope.Runtime
        };
    }

    private static AssemblyLoadContext Context(AssemblyCatalogSnapshot catalog)
        => AssemblyLoadContext.GetLoadContext(catalog.assemblies.Single(assembly =>
            assembly.GetName().Name == "Inno.Extensibility.Modules.TestModule"))!;

    private sealed class Participant : IAssemblyCatalogParticipant
    {
        internal string phase = string.Empty;
        internal Exception failure = new RetirementTimeoutException("Module work exceeded its retirement deadline.");
        internal Exception? activationFailure;

        public IAssemblyCatalogTransaction Prepare(AssemblyCatalogSnapshot catalog) => new Transaction(this);

        private sealed class Transaction(Participant owner) : IAssemblyCatalogTransaction
        {
            public object? context => null;

            public void Activate() => ThrowIfPending("activate");

            public void Complete() => ThrowIfPending("complete");

            public void Rollback() => ThrowIfPending("rollback");

            private void ThrowIfPending(string phase)
            {
                if (phase == "activate" && owner.activationFailure is not null)
                    throw owner.activationFailure;
                if (owner.phase == phase)
                    throw owner.failure;
            }
        }
    }
}
