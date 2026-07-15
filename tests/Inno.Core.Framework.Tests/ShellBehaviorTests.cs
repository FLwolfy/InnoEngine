using System;

using Inno.Assets;
using Inno.Core.Events;
using Inno.Core.Job;

using Xunit;

namespace Inno.Core.Framework.Tests;

public sealed class ShellBehaviorTests : IDisposable
{
    public void Dispose()
    {
        Shell.Shutdown();
    }

    [Fact]
    public void Initialize_WithNonPositiveFixedDelta_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Shell.Initialize(new ShellSettings
        {
            fixedDeltaTime = -0.01f
        }));
    }

    [Fact]
    public void Initialize_WithNonPositiveMaxFrameDelta_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Shell.Initialize(new ShellSettings
        {
            fixedDeltaTime = 1f / 60f,
            maxFrameDeltaTime = -0.01f
        }));
    }

    [Fact]
    public void Shell_AllowsOnlySingleLiveInstance()
    {
        Shell.Initialize(new ShellSettings());
        Assert.Throws<InvalidOperationException>(() => Shell.Initialize(new ShellSettings()));
    }

    [Fact]
    public void Shutdown_ReleasesSingleInstanceGuard()
    {
        Shell.Initialize(new ShellSettings());
        Shell.Shutdown();

        Shell.Initialize(new ShellSettings());
        var second = Shell.instance;
        Assert.NotNull(second);
    }

    [Fact]
    public void Tick_AfterShutdown_Throws()
    {
        Shell.Initialize(new ShellSettings());
        var shell = Shell.instance;
        Shell.Shutdown();
        Assert.Throws<ObjectDisposedException>(() => shell.Tick(0f, 0.016f));
    }

    [Fact]
    public void Tick_AdvancesLayers_AndFixedStep()
    {
        Shell.Initialize(new ShellSettings
        {
            fixedDeltaTime = 0.01f,
            maxFrameDeltaTime = 0.25f
        });
        var shell = Shell.instance;
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
        Shell.Initialize(new ShellSettings
        {
            fixedDeltaTime = 1f / 60f,
            maxFrameDeltaTime = 0.05f
        });
        var shell = Shell.instance;
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
        Shell.Initialize(new ShellSettings());
        var shell = Shell.instance;
        var layer = new EventProbeLayer("event-probe");
        shell.layerStack.PushLayer(layer);

        shell.eventDispatcher.Enqueue(new ProbeEvent(5));
        shell.Tick(0.01f, 0.01f);

        Assert.Equal(1, layer.receivedCount);
    }

    [Fact]
    public void JobSystem_IsAvailableByDefault_AndDrainsMainThreadCallbacks()
    {
        Shell.Initialize(new ShellSettings());
        var shell = Shell.instance;
        var layer = new JobProbeLayer("job-probe");
        shell.layerStack.PushLayer(layer);

        shell.Tick(0.01f, 0.01f);

        Assert.Equal(1, layer.callbackCount);
    }

    [Fact]
    public void SettingsCtor_AlwaysProvidesJobSystem()
    {
        Shell.Initialize(new ShellSettings
        {
            fixedDeltaTime = 1f / 60f,
            maxFrameDeltaTime = 0.25f
        });
        var shell = Shell.instance;

        Assert.True(JobSystem.workerCount >= 0);
    }

    [Fact]
    public void Initialize_InitializesAssetManager_AndShutdownShutsItDown()
    {
        Shell.Initialize(new ShellSettings());
        Assert.True(AssetManager.isInitialized);
        Assert.False(string.IsNullOrWhiteSpace(AssetManager.assetRoot));

        Shell.Shutdown();

        Assert.False(AssetManager.isInitialized);
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

    private sealed class JobProbeLayer(string name) : Layer(name)
    {
        private bool m_scheduled;

        public int callbackCount { get; private set; }

        public override void OnUpdate(float deltaTime)
        {
            if (m_scheduled)
                return;

            m_scheduled = true;
            _ = JobSystem.Schedule(() => JobSystem.RunOnMainThread(() => callbackCount++));
        }
    }
}
