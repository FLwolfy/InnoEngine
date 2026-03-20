using System;
using System.Threading;
using Inno.Core.Events;
using Inno.Core.Framework;
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
    public void Run_InvokesLifecycleCallbacks_AndTerminates()
    {
        using var shell = new Shell(fixedDeltaTime: 0.001f);

        var loadCount = 0;
        var setupCount = 0;
        var stepCount = 0;
        var drawCount = 0;
        var closeCount = 0;

        shell.SetOnLoad(() => loadCount++);
        shell.SetOnSetup(() => setupCount++);
        shell.SetOnStep(() =>
        {
            stepCount++;
            Thread.Sleep(1);
            if (stepCount >= 3)
            {
                shell.Terminate();
            }
        });
        shell.SetOnDraw(() => drawCount++);
        shell.SetOnClose(() => closeCount++);

        shell.Run();

        Assert.Equal(1, loadCount);
        Assert.Equal(1, setupCount);
        Assert.True(stepCount >= 3);
        Assert.True(drawCount >= 1);
        Assert.Equal(1, closeCount);
    }

    [Fact]
    public void Run_OnDisposedShell_Throws()
    {
        var shell = new Shell();
        shell.Dispose();

        Assert.Throws<ObjectDisposedException>(() => shell.Run());
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var shell = new Shell();
        shell.Dispose();
        shell.Dispose();
    }

    [Fact]
    public void Run_AdvancesFixedStepCallback()
    {
        using var shell = new Shell(fixedDeltaTime: 0.001f);

        var fixedCount = 0;
        var stepCount = 0;

        shell.SetOnFixedStep(() => fixedCount++);
        shell.SetOnStep(() =>
        {
            stepCount++;
            Thread.Sleep(2);
            if (fixedCount >= 2 || stepCount >= 20)
            {
                shell.Terminate();
            }
        });

        shell.Run();

        Assert.True(fixedCount >= 1);
        Assert.True(Time.fixedDeltaTime > 0f);
    }

    [Fact]
    public void SingleThreadMode_ExecutesDrawOnMainThread()
    {
        using var shell = new Shell(fixedDeltaTime: 0.001f, useBackgroundRenderThread: false);

        int drawThreadId = 0;
        var stepCount = 0;
        int mainThreadId = Environment.CurrentManagedThreadId;

        shell.SetOnDraw(() =>
        {
            Interlocked.CompareExchange(ref drawThreadId, Environment.CurrentManagedThreadId, 0);
        });
        shell.SetOnStep(() =>
        {
            stepCount++;
            Thread.Sleep(1);
            if (stepCount >= 3)
            {
                shell.Terminate();
            }
        });

        shell.Run();

        Assert.NotEqual(0, drawThreadId);
        Assert.Equal(mainThreadId, drawThreadId);
    }

    [Fact]
    public void BackgroundRenderThread_ExecutesDrawOutsideMainThread()
    {
        using var shell = new Shell(fixedDeltaTime: 0.001f, useBackgroundRenderThread: true);

        int drawThreadId = 0;
        var drawCount = 0;
        var stepCount = 0;
        var mainThreadId = Environment.CurrentManagedThreadId;

        shell.SetOnDraw(() =>
        {
            Interlocked.CompareExchange(ref drawThreadId, Environment.CurrentManagedThreadId, 0);
            Interlocked.Increment(ref drawCount);
        });
        shell.SetOnStep(() =>
        {
            stepCount++;
            Thread.Sleep(1);
            if (Volatile.Read(ref drawCount) >= 2 || stepCount >= 50)
            {
                shell.Terminate();
            }
        });

        shell.Run();

        Assert.True(drawCount >= 1);
        Assert.NotEqual(0, drawThreadId);
        Assert.NotEqual(mainThreadId, drawThreadId);
    }

    [Fact]
    public void OnClose_IsCalled_WhenStepThrows()
    {
        var shell = new Shell(fixedDeltaTime: 0.001f);
        var closeCount = 0;
        shell.SetOnClose(() => closeCount++);
        shell.SetOnStep(() => throw new InvalidOperationException("boom"));

        Assert.Throws<InvalidOperationException>(() => shell.Run());
        Assert.Equal(1, closeCount);

        using var next = new Shell();
        Assert.NotNull(next);
    }

    [Fact]
    public void EventDispatcher_QueuedEventDispatchedOnNextFlush()
    {
        using var shell = new Shell(fixedDeltaTime: 0.001f);
        var layer = new ProbeLayer("probe");
        shell.layerStack.PushLayer(layer);
        var stepCount = 0;

        shell.SetOnStep(() =>
        {
            stepCount++;
            if (stepCount == 1)
            {
                shell.eventDispatcher.Enqueue(new ProbeEvent(1));
            }

            if (stepCount >= 2)
            {
                shell.Terminate();
            }
        });

        shell.Run();

        Assert.Equal(1, layer.receivedCount);
    }

    private sealed class ProbeEvent(int value) : Event
    {
        public int value { get; } = value;
    }

    private sealed class ProbeLayer(string name) : Layer(name)
    {
        public int receivedCount { get; private set; }

        public override void OnAttach()
        {
            Listen<ProbeEvent>(_ => receivedCount++);
        }
    }
}
