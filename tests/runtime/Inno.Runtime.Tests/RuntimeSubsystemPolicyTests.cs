using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Inno.Core.Execution;
using Inno.Extensibility.Reload;
using Inno.Runtime.Contracts;
using Xunit;

namespace Inno.Runtime.Tests;

public sealed class RuntimeSubsystemPolicyTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "InnoSubsystemPolicyTests", Guid.NewGuid().ToString("N"));
    private readonly EngineHost m_host;

    public RuntimeSubsystemPolicyTests()
        => m_host = new EngineHostBuilder().UseMetadataCache(m_root).Build();

    public void Dispose()
    {
        m_host.Dispose();
        Directory.Delete(m_root, recursive: true);
    }

    [Fact]
    public void MissingRequiredCapabilityRejectsBeforeFactoryAllocation()
    {
        var factory = Create("test.required", capabilities: [new RuntimeCapabilityId("test.output")]);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => m_host.CreateHostPipeline([factory]));
        Assert.Contains("test.output", error.Message);
        Assert.Equal(0, factory.createCount);
        m_host.generations.EnsureReady("retry startup");
    }

    [Fact]
    public void SuppliedCapabilitiesAndDescriptorCollectionsAreImmutableSnapshots()
    {
        RuntimeCapabilityId[] required = [new("test.output")];
        var factory = Create("test.ready", capabilities: required);
        required[0] = new RuntimeCapabilityId("test.changed");
        RuntimeCapabilityId[] supplied = [new("test.output")];
        RuntimeSubsystemPipeline pipeline = m_host.CreateHostPipeline([factory], supplied);
        supplied[0] = new RuntimeCapabilityId("test.changed");
        Assert.Contains(new RuntimeCapabilityId("test.output"), factory.context!.capabilities);
        Assert.Equal(new RuntimeCapabilityId("test.output"), factory.descriptor.requiredCapabilities[0]);
        Assert.Equal("test.ready", Assert.Single(pipeline.descriptors).id.value);
        Assert.Empty(pipeline.startupDiagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalUnavailabilityPropagatesToOptionalConsumers(bool missingCapability)
    {
        var unavailable = Create("test.optional", RuntimeSubsystemRequirement.Optional,
            capabilities: missingCapability ? [new RuntimeCapabilityId("test.absent")] : []);
        unavailable.failCreate = !missingCapability;
        var consumer = Create("test.consumer", RuntimeSubsystemRequirement.Optional,
            dependencies: [unavailable.descriptor.id]);
        var available = Create("test.available");
        RuntimeSubsystemPipeline pipeline = m_host.CreateHostPipeline([consumer, available, unavailable]);
        Assert.Equal("test.available", Assert.Single(pipeline.descriptors).id.value);
        Assert.Equal(2, pipeline.startupDiagnostics.Count);
        Assert.Equal(0, consumer.createCount);
        Assert.Equal(missingCapability ? 0 : 1, unavailable.releaseCount);
        m_host.generations.EnsureReady("start another owner");
    }

    [Fact]
    public void RequiredConsumerCannotRunWithoutFailedOptionalDependency()
    {
        var optional = Create("test.optional", RuntimeSubsystemRequirement.Optional);
        optional.failCreate = true;
        var required = Create("test.required", dependencies: [optional.descriptor.id]);
        Assert.Throws<InvalidOperationException>(() => m_host.CreateHostPipeline([required, optional]));
        Assert.Equal(0, required.createCount);
        Assert.Equal(1, optional.releaseCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalStartupIsUnavailableOnlyAfterFactoryResourcesRetire(bool failAttach)
    {
        var optional = Create("test.optional", RuntimeSubsystemRequirement.Optional);
        optional.failCreate = !failAttach;
        optional.failAttach = failAttach;
        RuntimeSubsystemPipeline pipeline = m_host.CreateHostPipeline([optional]);
        Assert.Empty(pipeline.descriptors);
        Assert.Single(pipeline.startupDiagnostics);
        Assert.Equal(1, optional.releaseCount);
        pipeline.Dispose();
        Assert.Equal(1, optional.releaseCount);
    }

    [Fact]
    public void OptionalRetirementFailureFaultsInsteadOfDegrading()
    {
        var optional = Create("test.optional", RuntimeSubsystemRequirement.Optional);
        optional.failCreate = true;
        optional.failRelease = true;
        Assert.Throws<AggregateException>(() => m_host.CreateHostPipeline([optional]));
        Assert.Equal(GenerationState.Faulted, m_host.generations.state);
        Assert.Equal(1, optional.releaseCount);
        Assert.Throws<InvalidOperationException>(() => m_host.CreateHostPipeline([]));
        Assert.Throws<InvalidOperationException>(m_host.Dispose);
    }

    [Fact]
    public void OptionalMissingDependencyIsDiagnosedButCyclesStillReject()
    {
        var missing = Create("test.missing", RuntimeSubsystemRequirement.Optional,
            dependencies: [new RuntimeSubsystemId("test.absent")]);
        Assert.Single(m_host.CreateHostPipeline([missing]).startupDiagnostics);
        var a = Create("test.a", RuntimeSubsystemRequirement.Optional, dependencies: [new RuntimeSubsystemId("test.b")]);
        var b = Create("test.b", RuntimeSubsystemRequirement.Optional, dependencies: [a.descriptor.id]);
        Assert.Throws<InvalidOperationException>(() => m_host.CreateHostPipeline([a, b]));
        Assert.Equal(0, a.createCount);
        Assert.Equal(0, b.createCount);
    }

    [Fact]
    public void StartupTimeoutKeepsPendingHostPipelineAndPermanentlyFaultsAdmission()
    {
        string root = Path.Combine(m_root, "Timeout");
        EngineHost host = new EngineHostBuilder().UseMetadataCache(root)
            .UseRetirementTimeout(TimeSpan.FromTicks(1)).Build();
        var pending = new TaskCompletionSource();
        var factory = Create("test.timeout");
        factory.pending = pending.Task;
        factory.failCreate = true;
        Assert.Throws<RetirementTimeoutException>(() => host.CreateHostPipeline([factory]));
        Assert.Equal(0, factory.releaseCount);
        Assert.Equal(GenerationState.Faulted, host.generations.state);
        Assert.Throws<InvalidOperationException>(() => host.CreateHostPipeline([]));
        pending.SetResult();
        Assert.Throws<RetirementTimeoutException>(host.Dispose);
        Assert.Throws<RetirementTimeoutException>(host.Dispose);
        Assert.Equal(0, factory.releaseCount);
        Assert.Equal(GenerationState.Faulted, host.generations.state);
        Assert.Throws<RetirementTimeoutException>(host.modules.Dispose);
        host.logs.Dispose();
    }

    private static Factory Create(string id, RuntimeSubsystemRequirement requirement = RuntimeSubsystemRequirement.Required,
        IReadOnlyList<RuntimeSubsystemId>? dependencies = null, IReadOnlyList<RuntimeCapabilityId>? capabilities = null)
        => new(new RuntimeSubsystemDescriptor(new RuntimeSubsystemId(id), dependencies: dependencies,
            lifetime: RuntimeSubsystemLifetime.Host, requirement: requirement, requiredCapabilities: capabilities));

    private sealed class Factory(RuntimeSubsystemDescriptor descriptor) : IRuntimeSubsystemFactory
    {
        public RuntimeSubsystemDescriptor descriptor { get; } = descriptor;
        internal RuntimeSubsystemContext? context;
        internal int createCount;
        internal int releaseCount;
        internal bool failCreate;
        internal bool failAttach;
        internal bool failRelease;
        internal Task? pending;

        public IRuntimeSubsystem Create(RuntimeSubsystemContext context)
        {
            this.context = context;
            createCount++;
            context.resources.Own(new Resource(this));
            if (pending is not null)
                context.resources.Track(pending);
            if (failCreate)
                throw new InvalidOperationException("factory failed");
            return new Subsystem(this);
        }

        private sealed class Resource(Factory factory) : IDisposable
        {
            public void Dispose()
            {
                factory.releaseCount++;
                if (factory.failRelease)
                    throw new InvalidOperationException("resource retirement failed");
            }
        }

        private sealed class Subsystem(Factory factory) : RuntimeSubsystem
        {
            protected override void OnStart()
            {
                if (factory.failAttach)
                    throw new InvalidOperationException("attach failed");
            }
        }
    }
}
