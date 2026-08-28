using System;
using System.Collections.Generic;
using Inno.Core.Events;
using Inno.Core.Framework;
using Xunit;

namespace Inno.Core.Framework.Tests;

public sealed class LayerStackBehaviorTests
{
    [Fact]
    public void Constructor_WithNullHubFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LayerStack(null!));
    }

    [Fact]
    public void PushLayer_WithNull_Throws()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());

        Assert.Throws<ArgumentNullException>(() => layerStack.PushLayer(null!));
    }

    [Fact]
    public void PushOverlay_WithNull_Throws()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());

        Assert.Throws<ArgumentNullException>(() => layerStack.PushOverlay(null!));
    }

    [Fact]
    public void PopLayer_WithNull_Throws()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());

        Assert.Throws<ArgumentNullException>(() => layerStack.PopLayer(null!));
    }

    [Fact]
    public void PopOverlay_WithNull_Throws()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());

        Assert.Throws<ArgumentNullException>(() => layerStack.PopOverlay(null!));
    }

    [Fact]
    public void PushLayer_SubscribesAndReceivesDispatchedEvents()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());
        var layer = new CountingLayer("gameplay");

        layerStack.PushLayer(layer);
        dispatcher.Emit(new ProbeEvent(1));

        Assert.Equal(1, layer.receivedCount);
    }

    [Fact]
    public void PopLayer_UnsubscribesFromDispatcher()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());
        var layer = new CountingLayer("gameplay");

        layerStack.PushLayer(layer);
        dispatcher.Emit(new ProbeEvent(1));
        Assert.True(layerStack.PopLayer(layer));

        dispatcher.Emit(new ProbeEvent(2));
        Assert.Equal(1, layer.receivedCount);
    }

    [Fact]
    public void Overlay_PrecedesBaseLayer_AndCanStopPropagation()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());
        var order = new List<string>();
        var baseLayer = new OrderedLayer("base", order, handleInGlobal: false);
        var overlay = new OrderedLayer("overlay", order, handleInGlobal: true);

        layerStack.PushLayer(baseLayer);
        layerStack.PushOverlay(overlay);
        dispatcher.Emit(new ProbeEvent(1));

        Assert.Equal(["overlay"], order);
    }

    [Fact]
    public void Announce_DispatchesOnlyInsideCurrentLayerHub()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());
        var left = new AnnouncingLayer("left");
        var right = new AnnouncingLayer("right");

        layerStack.PushLayer(left);
        layerStack.PushLayer(right);

        left.AnnounceLocal(42);

        Assert.Equal(1, left.receivedCount);
        Assert.Equal(0, right.receivedCount);
    }

    [Fact]
    public void OnFixedUpdate_ForwardsToAllAttachedLayers()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());
        var layerA = new FixedCounterLayer("A");
        var layerB = new FixedCounterLayer("B");

        layerStack.PushLayer(layerA);
        layerStack.PushOverlay(layerB);
        layerStack.OnFixedUpdate(0.1f);
        layerStack.OnFixedUpdate(0.1f);

        Assert.Equal(2, layerA.fixedCount);
        Assert.Equal(2, layerB.fixedCount);
    }

    [Fact]
    public void PushSameLayerTwice_Throws()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());
        var layer = new CountingLayer("duplicate");

        layerStack.PushLayer(layer);

        Assert.Throws<InvalidOperationException>(() => layerStack.PushLayer(layer));
    }

    [Fact]
    public void PopLayer_WhenTargetIsOverlay_ReturnsFalse()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());
        var overlay = new CountingLayer("overlay");

        layerStack.PushOverlay(overlay);
        Assert.False(layerStack.PopLayer(overlay));
        Assert.Equal(1, layerStack.count);
    }

    [Fact]
    public void PopOverlay_WhenTargetIsBaseLayer_ReturnsFalse()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());
        var layer = new CountingLayer("base");

        layerStack.PushLayer(layer);
        Assert.False(layerStack.PopOverlay(layer));
        Assert.Equal(1, layerStack.count);
    }

    [Fact]
    public void PopUnknownLayer_ReturnsFalse()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());
        var unknown = new CountingLayer("unknown");

        Assert.False(layerStack.PopLayer(unknown));
        Assert.False(layerStack.PopOverlay(unknown));
    }

    [Fact]
    public void Clear_DetachesLayers_AndRemovesSubscriptions()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());
        var layerA = new LifecycleLayer("A");
        var layerB = new LifecycleLayer("B");

        layerStack.PushLayer(layerA);
        layerStack.PushOverlay(layerB);
        dispatcher.Emit(new ProbeEvent(1));
        layerStack.Clear();
        dispatcher.Emit(new ProbeEvent(2));

        Assert.Equal(0, layerStack.count);
        Assert.Equal(1, layerA.attachCount);
        Assert.Equal(1, layerA.detachCount);
        Assert.Equal(1, layerA.receivedCount);
        Assert.Equal(1, layerB.attachCount);
        Assert.Equal(1, layerB.detachCount);
        Assert.Equal(1, layerB.receivedCount);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var dispatcher = new EventDispatcher();
        var layerStack = new LayerStack(() => dispatcher.CreateHub());

        layerStack.Dispose();
        layerStack.Dispose();
    }

    [Fact]
    public void MethodsThrowAfterDispose()
    {
        var dispatcher = new EventDispatcher();
        var layerStack = new LayerStack(() => dispatcher.CreateHub());
        var layer = new CountingLayer("layer");
        layerStack.Dispose();

        Assert.Throws<ObjectDisposedException>(() => layerStack.PushLayer(layer));
        Assert.Throws<ObjectDisposedException>(() => layerStack.PushOverlay(layer));
        Assert.Throws<ObjectDisposedException>(() => layerStack.PopLayer(layer));
        Assert.Throws<ObjectDisposedException>(() => layerStack.PopOverlay(layer));
        Assert.Throws<ObjectDisposedException>(() => layerStack.OnUpdate(0.1f));
        Assert.Throws<ObjectDisposedException>(() => layerStack.OnFixedUpdate(0.1f));
        Assert.Throws<ObjectDisposedException>(() => layerStack.OnLateUpdate(0.1f));
        Assert.Throws<ObjectDisposedException>(() => layerStack.RenderFrame(0.1f));
    }

    [Fact]
    public void RenderFrame_ExecutesPreparationAndSubmissionForward_ThenCompletionReverse()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());
        var calls = new List<string>();

        layerStack.PushLayer(new RenderLifecycleLayer("A", calls));
        layerStack.PushOverlay(new RenderLifecycleLayer("B", calls));

        layerStack.RenderFrame(0.1f);

        Assert.Equal(
            ["A.Before", "B.Before", "A.Render", "B.Render", "B.After", "A.After"],
            calls);
    }

    [Fact]
    public void RenderFrame_WhenSubmissionFails_UnwindsEveryPreparedLayer()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());
        var calls = new List<string>();

        layerStack.PushLayer(new RenderLifecycleLayer("A", calls));
        layerStack.PushLayer(new RenderLifecycleLayer("B", calls, throwDuringRender: true));
        layerStack.PushOverlay(new RenderLifecycleLayer("C", calls));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => layerStack.RenderFrame(0.1f));

        Assert.Equal("B render failed.", exception.Message);
        Assert.Equal(
            ["A.Before", "B.Before", "C.Before", "A.Render", "B.Render", "C.After", "B.After", "A.After"],
            calls);
    }

    [Fact]
    public void RenderFrame_WhenPreparationFails_UnwindsOnlyCompletedPreparation()
    {
        var dispatcher = new EventDispatcher();
        using var layerStack = new LayerStack(() => dispatcher.CreateHub());
        var calls = new List<string>();

        layerStack.PushLayer(new RenderLifecycleLayer("A", calls));
        layerStack.PushLayer(new RenderLifecycleLayer("B", calls, throwDuringBefore: true));
        layerStack.PushOverlay(new RenderLifecycleLayer("C", calls));

        Assert.Throws<InvalidOperationException>(() => layerStack.RenderFrame(0.1f));

        Assert.Equal(["A.Before", "B.Before", "A.After"], calls);
    }

    private sealed class ProbeEvent(int value) : Event
    {
        public int value { get; } = value;
    }

    private sealed class CountingLayer(string name) : Layer(name)
    {
        public int receivedCount { get; private set; }

        public override void OnAttach()
        {
            Listen<ProbeEvent>(_ => receivedCount++);
        }
    }

    private sealed class OrderedLayer(string name, List<string> order, bool handleInGlobal) : Layer(name)
    {
        public override void OnAttach()
        {
            Listen<ProbeEvent>(e =>
            {
                order.Add(name);
                if (handleInGlobal)
                {
                    e.HandleInGlobal();
                }
            });
        }
    }

    private sealed class AnnouncingLayer(string name) : Layer(name)
    {
        public int receivedCount { get; private set; }

        public override void OnAttach()
        {
            Listen<ProbeEvent>(_ => receivedCount++);
        }

        public void AnnounceLocal(int value)
        {
            Announce(new ProbeEvent(value));
        }
    }

    private sealed class FixedCounterLayer(string name) : Layer(name)
    {
        public int fixedCount { get; private set; }

        public override void OnFixedUpdate(float fixedDeltaTime)
        {
            fixedCount++;
        }
    }

    private sealed class LifecycleLayer(string name) : Layer(name)
    {
        public int attachCount { get; private set; }
        public int detachCount { get; private set; }
        public int receivedCount { get; private set; }

        public override void OnAttach()
        {
            attachCount++;
            Listen<ProbeEvent>(_ => receivedCount++);
        }

        public override void OnDetach()
        {
            detachCount++;
        }
    }

    private sealed class RenderLifecycleLayer(
        string name,
        List<string> calls,
        bool throwDuringBefore = false,
        bool throwDuringRender = false) : Layer(name)
    {
        public override void OnBeforeRender(float deltaTime)
        {
            calls.Add($"{name}.Before");
            if (throwDuringBefore)
            {
                throw new InvalidOperationException($"{name} before failed.");
            }
        }

        public override void OnRender(float deltaTime)
        {
            calls.Add($"{name}.Render");
            if (throwDuringRender)
            {
                throw new InvalidOperationException($"{name} render failed.");
            }
        }

        public override void OnAfterRender(float deltaTime)
        {
            calls.Add($"{name}.After");
        }
    }
}
