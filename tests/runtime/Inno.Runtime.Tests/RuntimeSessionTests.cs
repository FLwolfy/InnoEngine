using Inno.Runtime.Contracts;
using System;
using System.IO;
using System.Collections.Generic;

using Inno.References;
using Inno.Runtime;
using Inno.Scene;

using Xunit;

namespace Inno.Runtime.Tests;

public sealed class RuntimeSessionTests : IDisposable
{
    private readonly string m_root = Path.Combine(
        Path.GetTempPath(),
        "InnoRuntimeSessionTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void MultipleHostsAndSessionsKeepSceneAndTimeStateIsolated()
    {
        using EngineHost firstHost = CreateHost("First");
        using EngineHost secondHost = CreateHost("Second");
        using RuntimeSession first = firstHost.CreateSession(CreateOptions("first", RuntimeSessionKind.Play));
        using RuntimeSession second = secondHost.CreateSession(CreateOptions("second", RuntimeSessionKind.Play));

        using (first.EnterExecutionScope())
            _ = SceneManager.LoadNewScene("First Scene");
        using (second.EnterExecutionScope())
            _ = SceneManager.LoadNewScene("Second Scene");

        first.Tick(0.02f);
        second.Tick(0.04f);

        using (first.EnterExecutionScope())
        {
            Assert.Equal("First Scene", Assert.Single(SceneManager.loadedScenes).name);
            Assert.Equal(0.02f, Time.time);
            Assert.Equal(0.02f, Time.deltaTime);
        }
        using (second.EnterExecutionScope())
        {
            Assert.Equal("Second Scene", Assert.Single(SceneManager.loadedScenes).name);
            Assert.Equal(0.04f, Time.time);
            Assert.Equal(0.04f, Time.deltaTime);
        }
    }

    [Fact]
    public void ScriptFacadesRejectCallsOutsideAnActiveSession()
    {
        Assert.Throws<InvalidOperationException>(() => _ = SceneManager.loadedScenes);
        Assert.Throws<InvalidOperationException>(() => _ = Time.deltaTime);
    }

    [Fact]
    public void TimeSeparatesScaledUnscaledAndPausedProgress()
    {
        using EngineHost host = CreateHost("Time");
        using RuntimeSession session = host.CreateSession(CreateOptions("time", RuntimeSessionKind.Play));

        session.Tick(0.1f);
        session.timeScale = 0.5f;
        session.Tick(0.1f);
        session.isPaused = true;
        session.Tick(0.1f);

        using IDisposable scope = session.EnterExecutionScope();
        Assert.Equal(0.15f, Time.time, precision: 5);
        Assert.Equal(0.3f, Time.unscaledTime, precision: 5);
        Assert.Equal(0f, Time.deltaTime);
        Assert.Equal(0.1f, Time.unscaledDeltaTime, precision: 5);
        Assert.Equal(0.5f, Time.timeScale);
        Assert.True(Time.isPaused);
        Assert.Equal(3, Time.frameCount);
    }

    [Fact]
    public void RuntimeSubsystemsFollowDependenciesAndReleaseInReverseOrder()
    {
        using EngineHost host = CreateHost("Features");
        var events = new List<string>();
        var first = new ProbeFeatureFactory("tests.first", [], events);
        var second = new ProbeFeatureFactory(
            "tests.second",
            [new RuntimeSubsystemId("tests.first")],
            events);
        RuntimeSessionOptions baseline = CreateOptions("features", RuntimeSessionKind.Play);
        var options = new RuntimeSessionOptions
        {
            kind = baseline.kind,
            applicationId = baseline.applicationId,
            persistentDataDirectory = baseline.persistentDataDirectory,
            fixedDeltaTime = baseline.fixedDeltaTime,
            jobExecutionMode = baseline.jobExecutionMode,
            createSubsystems = owner => [second, first]
        };
        RuntimeSession session = host.CreateSession(options);

        session.Tick(0.01f);
        session.Dispose();

        Assert.Equal(
        [
            "attach:tests.first",
            "attach:tests.second",
            "begin:tests.first",
            "begin:tests.second",
            "update:tests.first",
            "update:tests.second",
            "late:tests.first",
            "late:tests.second",
            "before-render:tests.first",
            "before-render:tests.second",
            "render:tests.first",
            "render:tests.second",
            "after-render:tests.second",
            "after-render:tests.first",
            "end:tests.second",
            "end:tests.first",
            "detach:tests.second",
            "dispose:tests.second",
            "detach:tests.first",
            "dispose:tests.first"
        ], events);
    }

    [Fact]
    public void SessionPublishesOwnerProvidedReferenceResolversAndPreservesValidatedBudgets()
    {
        using EngineHost host = CreateHost("References");
        RuntimeSessionOptions baseline = CreateOptions("references", RuntimeSessionKind.Play);
        var kind = new ReferenceKindId("tests.runtime.reference");
        var resolver = new MissingResolver(kind);
        var options = new RuntimeSessionOptions
        {
            kind = baseline.kind,
            applicationId = baseline.applicationId,
            persistentDataDirectory = baseline.persistentDataDirectory,
            assetResidencyBudgetBytes = 4096,
            fixedDeltaTime = baseline.fixedDeltaTime,
            jobExecutionMode = baseline.jobExecutionMode,
            referenceResolvers = [resolver]
        };

        using RuntimeSession session = host.CreateSession(options);
        var descriptor = new ReferenceDescriptor(kind, Guid.NewGuid(), lastKnownName: "Unavailable");

        Assert.Equal(4096, session.options.assetResidencyBudgetBytes);
        Assert.Equal(1, session.references.generation);
        ReferenceResolution resolution = session.references.Resolve(descriptor);
        Assert.Equal(ReferenceResolutionState.Missing, resolution.state);
        Assert.Same(descriptor, resolution.descriptor);
    }

    [Fact]
    public void DisposedHostRejectsNewSessions()
    {
        EngineHost host = CreateHost("Disposed");
        host.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => host.CreateSession(CreateOptions("disposed", RuntimeSessionKind.Edit)));
    }

    public void Dispose()
    {
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    private EngineHost CreateHost(string name)
        => new EngineHostBuilder()
            .UseMetadataCache(Path.Combine(m_root, name, "Metadata"))
            .Build();

    private RuntimeSessionOptions CreateOptions(string applicationId, RuntimeSessionKind kind)
        => new()
        {
            kind = kind,
            applicationId = applicationId,
            persistentDataDirectory = Path.Combine(m_root, "Persistent", applicationId),
            fixedDeltaTime = 0.02f,
            jobExecutionMode = RuntimeJobExecutionMode.SingleThread
        };

    private sealed class ProbeFeatureFactory(
        string id,
        IReadOnlyList<RuntimeSubsystemId> dependencies,
        List<string> events) : IRuntimeSubsystemFactory
    {
        public RuntimeSubsystemDescriptor descriptor { get; } = new(
            new RuntimeSubsystemId(id),
            dependencies: dependencies);

        public IRuntimeSubsystem Create(RuntimeSubsystemContext context)
            => new ProbeFeature(descriptor.id.value, events);
    }

    private sealed class ProbeFeature(string id, List<string> events) : RuntimeSubsystem
    {
        protected override void OnStart() => events.Add($"attach:{id}");

        protected override void OnBeginFrame(RuntimeFrame frame) => events.Add($"begin:{id}");

        protected override void OnUpdate(RuntimeFrame frame) => events.Add($"update:{id}");

        protected override void OnLateUpdate(RuntimeFrame frame) => events.Add($"late:{id}");

        protected override void OnPrepareOutput(RuntimeFrame frame) => events.Add($"before-render:{id}");

        protected override void OnProduceOutput(RuntimeFrame frame) => events.Add($"render:{id}");

        protected override void OnCompleteOutput(RuntimeFrame frame) => events.Add($"after-render:{id}");

        protected override void OnEndFrame(RuntimeFrame frame) => events.Add($"end:{id}");

        protected override void OnStop()
        {
            events.Add($"detach:{id}");
            events.Add($"dispose:{id}");
        }
    }

    private sealed class MissingResolver(ReferenceKindId kind) : IReferenceResolver
    {
        public ReferenceKindId kindId { get; } = kind;

        public ReferenceResolution Resolve(ReferenceDescriptor descriptor)
            => new(descriptor, ReferenceResolutionState.Missing);
    }
}
