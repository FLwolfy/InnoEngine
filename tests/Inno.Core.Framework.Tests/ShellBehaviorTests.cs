using System;

using Inno.Core.Events;
using Inno.Core.Job;

using Xunit;

namespace Inno.Core.Framework.Tests;

public sealed class ShellBehaviorTests
{
    [Fact]
    public void Constructor_WithNonPositiveFixedDelta_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Shell(0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Shell(-0.01f));
    }

    [Fact]
    public void Constructor_WithNonPositiveMaxFrameDelta_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Shell(new ShellSettings
        {
            fixedDeltaTime = 1f / 60f,
            maxFrameDeltaTime = 0f
        }));
    }

    [Fact]
    public void Shell_AllowsOnlySingleLiveInstance()
    {
        using var first = new Shell();
        Assert.Throws<InvalidOperationException>(() => new Shell());
    }

    [Fact]
    public void Dispose_ReleasesSingleInstanceGuard()
    {
        var first = new Shell();
        first.Dispose();

        using var second = new Shell();
        Assert.NotNull(second);
    }

    [Fact]
    public void Tick_OnDisposedShell_Throws()
    {
        var shell = new Shell();
        shell.Dispose();
        Assert.Throws<ObjectDisposedException>(() => shell.Tick(0f, 0.016f));
    }

    [Fact]
    public void Tick_AdvancesLayers_AndFixedStep()
    {
        using var shell = new Shell(new ShellSettings
        {
            fixedDeltaTime = 0.01f,
            maxFrameDeltaTime = 0.25f
        });
        var layer = new ProbeLayer("probe");
        shell.layerStack.PushLayer(layer);

        shell.Tick(0.01f, 0.01f);
        shell.Tick(0.02f, 0.01f);

        Assert.Equal(2, layer.updateCount);
        Assert.Equal(2, layer.lateUpdateCount);
        Assert.True(layer.fixedCount >= 2);
    }

    [Fact]
    public void Tick_ClampsDeltaByMaxFrameDelta()
    {
        using var shell = new Shell(new ShellSettings
        {
            fixedDeltaTime = 1f / 60f,
            maxFrameDeltaTime = 0.05f
        });
        var layer = new ProbeLayer("probe");
        shell.layerStack.PushLayer(layer);

        shell.Tick(1f, 1f);

        Assert.True(layer.lastUpdateDelta <= 0.05f + 0.0001f);
        Assert.True(Math.Abs(Time.deltaTime - layer.lastUpdateDelta) < 0.0001f);
        Assert.True(Math.Abs(layer.lastLateUpdateDelta - layer.lastUpdateDelta) < 0.0001f);
    }

    [Fact]
    public void EventDispatcher_QueuedEventDispatchedOnTick()
    {
        using var shell = new Shell();
        var layer = new EventProbeLayer("event-probe");
        shell.layerStack.PushLayer(layer);

        shell.eventDispatcher.Enqueue(new ProbeEvent(5));
        shell.Tick(0.01f, 0.01f);

        Assert.Equal(1, layer.receivedCount);
    }

    [Fact]
    public void JobSystem_IsAvailableByDefault_AndDrainsMainThreadCallbacks()
    {
        using var shell = new Shell();
        var callbacks = 0;

        _ = JobSystem.Schedule(() => JobSystem.RunOnMainThread(() => callbacks++));
        shell.Tick(0.01f, 0.01f);

        Assert.Equal(1, callbacks);
    }

    [Fact]
    public void SettingsCtor_AlwaysProvidesJobSystem()
    {
        using var shell = new Shell(new ShellSettings
        {
            fixedDeltaTime = 1f / 60f,
            maxFrameDeltaTime = 0.25f
        });

        Assert.True(JobSystem.workerCount >= 0);
    }

    private sealed class ProbeEvent(int value) : Event
    {
        public int value { get; } = value;
    }

    private sealed class ProbeLayer(string name) : Layer(name)
    {
        public int fixedCount { get; private set; }
        public int updateCount { get; private set; }
        public int lateUpdateCount { get; private set; }
        public float lastUpdateDelta { get; private set; }
        public float lastLateUpdateDelta { get; private set; }

        public override void OnFixedUpdate(float fixedDeltaTime)
        {
            fixedCount++;
        }

        public override void OnUpdate(float deltaTime)
        {
            updateCount++;
            lastUpdateDelta = deltaTime;
        }

        public override void OnLateUpdate(float deltaTime)
        {
            lateUpdateCount++;
            lastLateUpdateDelta = deltaTime;
        }
    }

    private sealed class EventProbeLayer(string name) : Layer(name)
    {
        public int receivedCount { get; private set; }

        public override void OnAttach()
        {
            _ = Listen<ProbeEvent>(_ => receivedCount++);
        }
    }
}
