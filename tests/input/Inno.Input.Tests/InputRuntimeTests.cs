using Inno.Runtime.Contracts;
using System;
using System.Collections.Generic;
using System.IO;

using Inno.Core.Events;
using Inno.Core.Input;
using Inno.Input.Runtime;
using Inno.Adapter.Input.Sdl3;
using Inno.Runtime;
using Xunit;

namespace Inno.Input.Tests;

public sealed class InputRuntimeTests
{
    [Fact]
    public void SdlAdapterCapturesTransitionsAndClearsTransientState()
    {
        using var backend = new Sdl3InputBackend(windowId: 7);
        backend.ProcessEvent(new KeyPressedEvent(7, KeyCode.Space));
        backend.ProcessEvent(new MouseMovedEvent(7, 10f, 20f));
        backend.ProcessEvent(new MouseScrolledEvent(7, 0f, 2f));

        InputSnapshot first = backend.Capture(3);
        InputSnapshot second = backend.Capture(4);

        Assert.True(first.IsKeyDown(KeyCode.Space));
        Assert.True(first.WasKeyPressed(KeyCode.Space));
        Assert.Equal(10f, first.mousePosition.x);
        Assert.Equal(2f, first.scrollDelta.y);
        Assert.True(second.IsKeyDown(KeyCode.Space));
        Assert.False(second.WasKeyPressed(KeyCode.Space));
        Assert.Equal(0f, second.mouseDelta.x);
        Assert.Equal(0f, second.scrollDelta.y);
    }

    [Fact]
    public void RuntimeSubsystemBindsSnapshotAcrossSimulationPhases()
    {
        string root = Path.Combine(Path.GetTempPath(), "InnoInputRuntimeTests", Guid.NewGuid().ToString("N"));
        using var source = new Sdl3InputSource(windowId: 1);
        var observations = new List<bool>();
        using EngineHost host = new EngineHostBuilder()
            .UseMetadataCache(Path.Combine(root, "Metadata"))
            .Build();
        var options = new RuntimeSessionOptions
        {
            kind = RuntimeSessionKind.Play,
            applicationId = "tests.input",
            persistentDataDirectory = Path.Combine(root, "tests.input"),
            jobExecutionMode = RuntimeJobExecutionMode.SingleThread,
            createSubsystems = owner =>
            [
                new InputRuntimeFactory(_ => source.CreateBackend()),
                new InputProbeFactory(observations)
            ]
        };

        using (RuntimeSession session = host.CreateSession(options))
        {
            source.ProcessEvent(new KeyPressedEvent(1, KeyCode.Enter));
            session.Tick(0.01f);
        }

        Assert.Equal([true, true], observations);
        Assert.Throws<InvalidOperationException>(() => _ = Input.snapshot);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class InputProbeFactory(List<bool> observations) : IRuntimeSubsystemFactory
    {
        public RuntimeSubsystemDescriptor descriptor { get; } = new(
            new RuntimeSubsystemId("tests.input.probe"),
            dependencies: [new RuntimeSubsystemId("inno.runtime.input")]);

        public IRuntimeSubsystem Create(RuntimeSubsystemContext context) => new InputProbe(observations);
    }

    private sealed class InputProbe(List<bool> observations) : RuntimeSubsystem
    {
        protected override void OnUpdate(RuntimeFrame frame)
            => observations.Add(Input.IsKeyDown(KeyCode.Enter));

        protected override void OnLateUpdate(RuntimeFrame frame)
            => observations.Add(Input.WasKeyPressed(KeyCode.Enter));
    }
}
