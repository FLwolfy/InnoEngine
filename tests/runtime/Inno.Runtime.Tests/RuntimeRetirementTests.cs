using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inno.Core.Execution;
using Inno.Runtime.Contracts;
using Xunit;

namespace Inno.Runtime.Tests;

public sealed class RuntimeRetirementTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FailedStartupDrainsBeforeReleasingFactoryResources(bool hostLifetime, bool failAttach)
    {
        string root = Path.Combine(Path.GetTempPath(), "InnoStartupTests", Guid.NewGuid().ToString("N"));
        using EngineHost host = new EngineHostBuilder().UseMetadataCache(Path.Combine(root, "Metadata")).Build();
        var resource = new Resource();
        var factory = new FailingFactory(resource, hostLifetime, failAttach);
        try
        {
            Exception error = hostLifetime
                ? Assert.Throws<InvalidOperationException>(() => host.CreateHostPipeline([factory]))
                : Assert.Throws<InvalidOperationException>(() => host.CreateSession(new RuntimeSessionOptions
                {
                    applicationId = "startup.test",
                    kind = RuntimeSessionKind.Play,
                    persistentDataDirectory = Path.Combine(root, "startup.test"),
                    jobExecutionMode = RuntimeJobExecutionMode.SingleThread,
                    createSubsystems = _ => [factory]
                }));
            Assert.Equal("startup failure", error.Message);
            Assert.True(factory.observedRetainedResource);
            Assert.True(resource.disposed);
            Assert.Equal(1, resource.disposeCount);
            host.generations.EnsureReady("start after compensated startup");
            host.Dispose();
            Assert.Equal(1, resource.disposeCount);
        }
        finally
        {
            host.Dispose();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StopHookPendingRetainsDependenciesWithoutRepeatingCompletedOwners()
    {
        string root = Path.Combine(Path.GetTempPath(), "InnoStopTests", Guid.NewGuid().ToString("N"));
        using EngineHost host = new EngineHostBuilder().UseMetadataCache(Path.Combine(root, "Metadata")).Build();
        var pending = new StopPendingSubsystem();
        var resource = new Resource();
        RuntimeSession session = host.CreateSession(new RuntimeSessionOptions
        {
            applicationId = "stop.test",
            kind = RuntimeSessionKind.Play,
            persistentDataDirectory = Path.Combine(root, "stop.test"),
            jobExecutionMode = RuntimeJobExecutionMode.SingleThread,
            createSubsystems = _ => [new SimpleFactory(pending, resource)]
        });
        Assert.Throws<RetirementPendingException>(session.Dispose);
        Assert.False(resource.disposed);
        pending.ready = true;
        session.Dispose();
        Assert.True(resource.disposed);
        Assert.Equal(2, pending.stopAttempts);
        host.Dispose();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void PendingSessionRetainsDependenciesAndCanBeRetriedThroughHost()
    {
        string root = Path.Combine(Path.GetTempPath(), "InnoRetirementTests", Guid.NewGuid().ToString("N"));
        var pending = new PendingSubsystem();
        var resource = new Resource();
        using EngineHost host = new EngineHostBuilder().UseMetadataCache(Path.Combine(root, "Metadata")).Build();
        try
        {
            RuntimeSession session = host.CreateSession(new RuntimeSessionOptions
            {
                applicationId = "retirement.test",
                kind = RuntimeSessionKind.Play,
                persistentDataDirectory = Path.Combine(root, "retirement.test"),
                jobExecutionMode = RuntimeJobExecutionMode.SingleThread,
                createSubsystems = _ => [new Factory(pending, resource)]
            });

            Assert.Throws<RetirementPendingException>(session.Dispose);
            Assert.False(resource.disposed);
            Assert.False(pending.stopped);
            Assert.Throws<RetirementPendingException>(host.Dispose);
            Assert.False(resource.disposed);
            pending.work.SetResult();
            host.Dispose();
            Assert.True(pending.stopped);
            Assert.True(resource.disposed);
            session.Dispose();
        }
        finally
        {
            pending.work.TrySetResult();
            host.Dispose();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Factory(PendingSubsystem subsystem, Resource resource) : IRuntimeSubsystemFactory
    {
        public RuntimeSubsystemDescriptor descriptor { get; } = new(new RuntimeSubsystemId("tests.pending"));
        public IRuntimeSubsystem Create(RuntimeSubsystemContext context)
        {
            context.resources.Own(resource);
            return subsystem;
        }
    }

    private sealed class PendingSubsystem : RuntimeSubsystem
    {
        internal TaskCompletionSource work { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool stopped { get; private set; }
        protected override void OnStart() => lifetime.Track(work.Task);
        protected override void OnStop() => stopped = true;
    }

    private sealed class Resource : IDisposable
    {
        internal bool disposed { get; private set; }
        internal int disposeCount { get; private set; }
        public void Dispose() { disposed = true; disposeCount++; }
    }

    private sealed class FailingFactory(Resource resource, bool hostLifetime, bool failAttach) : IRuntimeSubsystemFactory
    {
        public RuntimeSubsystemDescriptor descriptor { get; } = new(new RuntimeSubsystemId("tests.failure"),
            lifetime: hostLifetime ? RuntimeSubsystemLifetime.Host : RuntimeSubsystemLifetime.Session);
        internal bool observedRetainedResource;

        public IRuntimeSubsystem Create(RuntimeSubsystemContext context)
        {
            context.resources.Own(resource);
            if (failAttach)
                return new FailingSubsystem(this, resource);
            context.resources.Track(Drain(context.resources.cancellationToken));
            throw new InvalidOperationException("startup failure");
        }

        internal async Task Drain(CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            observedRetainedResource = !resource.disposed;
        }

        private sealed class FailingSubsystem(FailingFactory factory, Resource resource) : RuntimeSubsystem
        {
            protected override void OnStart()
            {
                lifetime.Track(factory.Drain(lifetime.cancellationToken));
                throw new InvalidOperationException("startup failure");
            }
            protected override void OnStop() => Assert.False(resource.disposed);
        }
    }

    private sealed class SimpleFactory(IRuntimeSubsystem subsystem, Resource resource) : IRuntimeSubsystemFactory
    {
        public RuntimeSubsystemDescriptor descriptor { get; } = new(new RuntimeSubsystemId("tests.stop"));
        public IRuntimeSubsystem Create(RuntimeSubsystemContext context)
        {
            context.resources.Own(resource);
            return subsystem;
        }
    }

    private sealed class StopPendingSubsystem : RuntimeSubsystem
    {
        internal bool ready;
        internal int stopAttempts;
        protected override void OnStop()
        {
            stopAttempts++;
            if (!ready)
                throw new RetirementPendingException("native drain pending");
        }
    }
}
