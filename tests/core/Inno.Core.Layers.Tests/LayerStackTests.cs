using System;
using System.Collections.Generic;

using Inno.Core.Events;
using Inno.Core.Execution;
using Inno.Core.Layers;

using Xunit;

namespace Inno.Core.Layers.Tests;

public sealed class LayerStackTests
{
    [Fact]
    public void PushLayer_InsertsBelowExistingOverlays()
    {
        var dispatcher = new EventDispatcher();
        using var stack = new LayerStack(() => dispatcher.CreateHub());
        var first = new ProbeLayer("first");
        var overlay = new ProbeLayer("overlay");
        var second = new ProbeLayer("second");

        stack.PushLayer(first);
        stack.PushOverlay(overlay);
        stack.PushLayer(second);

        Assert.Equal(3, stack.count);
        Assert.Same(first, stack[0]);
        Assert.Same(second, stack[1]);
        Assert.Same(overlay, stack[2]);
    }

    [Fact]
    public void PushLayer_RejectsDuplicateInstance()
    {
        var dispatcher = new EventDispatcher();
        using var stack = new LayerStack(() => dispatcher.CreateHub());
        var layer = new ProbeLayer("duplicate");
        stack.PushLayer(layer);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => stack.PushOverlay(layer));

        Assert.Contains("already attached", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, stack.count);
    }

    [Fact]
    public void EventDispatch_RunsTopmostOverlayFirst()
    {
        var dispatcher = new EventDispatcher();
        using var stack = new LayerStack(() => dispatcher.CreateHub());
        var order = new List<string>();
        stack.PushLayer(new ProbeLayer("base", order));
        stack.PushOverlay(new ProbeLayer("overlay", order));

        dispatcher.Emit(new ProbeEvent());

        Assert.Equal(["overlay", "base"], order);
    }

    [Fact]
    public void RenderFrame_UnwindsPreparedLayersInReverseOrderAfterFailure()
    {
        var dispatcher = new EventDispatcher();
        using var stack = new LayerStack(() => dispatcher.CreateHub());
        var calls = new List<string>();
        stack.PushLayer(new ProbeLayer("first", calls));
        stack.PushLayer(new ProbeLayer("second", calls) { throwOnRender = true });

        _ = Assert.Throws<InvalidOperationException>(() => stack.RenderFrame(0.25f));

        Assert.Equal(
            ["first.before", "second.before", "first.render", "second.render", "second.after", "first.after"],
            calls);
    }

    [Fact]
    public void PopMethods_RespectBaseAndOverlayRegions()
    {
        var dispatcher = new EventDispatcher();
        using var stack = new LayerStack(() => dispatcher.CreateHub());
        var layer = new ProbeLayer("base");
        var overlay = new ProbeLayer("overlay");
        stack.PushLayer(layer);
        stack.PushOverlay(overlay);

        Assert.False(stack.PopOverlay(layer));
        Assert.False(stack.PopLayer(overlay));
        Assert.True(stack.PopOverlay(overlay));
        Assert.True(stack.PopLayer(layer));
        Assert.Equal(1, overlay.detachCount);
        Assert.Equal(1, layer.detachCount);
    }

    [Fact]
    public void FailedAttach_ReleasesSubscriptionsAndLeavesStackReusable()
    {
        var dispatcher = new EventDispatcher();
        using var stack = new LayerStack(() => dispatcher.CreateHub());
        var failed = new ProbeLayer("failed") { throwOnAttach = true };

        _ = Assert.Throws<InvalidOperationException>(() => stack.PushLayer(failed));
        dispatcher.Emit(new ProbeEvent());
        stack.PushLayer(new ProbeLayer("healthy"));

        Assert.Equal(0, failed.eventCount);
        Assert.Equal(1, failed.detachCount);
        Assert.Equal(1, stack.count);
    }

    [Fact]
    public void PendingDetachRetainsCurrentAndLowerLayersAndRejectsFrames()
    {
        var dispatcher = new EventDispatcher();
        var stack = new LayerStack(() => dispatcher.CreateHub());
        var lower = new ProbeLayer("lower");
        var pending = new ProbeLayer("pending") { pendingDetaches = 1 };
        stack.PushLayer(lower);
        stack.PushOverlay(pending);

        Assert.Throws<RetirementPendingException>(stack.Dispose);
        Assert.Equal(2, stack.count);
        Assert.Equal(0, lower.detachCount);
        Assert.Throws<InvalidOperationException>(() => stack.OnUpdate(0));
        Assert.Throws<InvalidOperationException>(() => stack.PushOverlay(new ProbeLayer("rejected")));
        stack.Dispose();
        stack.Dispose();
        Assert.Equal(2, pending.detachCount);
        Assert.Equal(1, lower.detachCount);
        Assert.Equal(0, stack.count);
    }

    [Fact]
    public void FailureBeforePendingIsReportedAfterRemainingLayersDrain()
    {
        var dispatcher = new EventDispatcher();
        var stack = new LayerStack(() => dispatcher.CreateHub());
        var pending = new ProbeLayer("pending") { pendingDetaches = 1 };
        var failed = new ProbeLayer("failed") { throwOnDetach = true };
        stack.PushLayer(pending);
        stack.PushOverlay(failed);

        Assert.Throws<RetirementPendingException>(stack.Clear);
        Assert.Equal(1, stack.count);
        Assert.Throws<InvalidOperationException>(stack.Clear);
        Assert.Equal(1, failed.detachCount);
        stack.PushLayer(new ProbeLayer("reusable"));
        stack.Dispose();
    }

    [Fact]
    public void PartialAttachKeepsOwnershipUntilCompensationDrains()
    {
        var dispatcher = new EventDispatcher();
        var stack = new LayerStack(() => dispatcher.CreateHub());
        var failed = new ProbeLayer("failed") { throwOnAttach = true, pendingDetaches = 1 };

        Assert.Throws<RetirementPendingException>(() => stack.PushLayer(failed));
        Assert.Same(failed, stack[0]);
        Assert.Throws<InvalidOperationException>(stack.Clear);
        Assert.Equal(2, failed.detachCount);
        Assert.Equal(0, stack.count);
        stack.Dispose();
    }

    [Fact]
    public void CrossStackAttachmentDoesNotDetachTheOriginalOwner()
    {
        var dispatcher = new EventDispatcher();
        using var first = new LayerStack(() => dispatcher.CreateHub());
        using var second = new LayerStack(() => dispatcher.CreateHub());
        var layer = new ProbeLayer("owned");
        first.PushLayer(layer);
        Assert.Throws<InvalidOperationException>(() => second.PushLayer(layer));
        Assert.Equal(1, first.count);
        Assert.Equal(0, second.count);
        Assert.Equal(0, layer.detachCount);
    }

    private sealed class ProbeEvent : Event
    {
    }

    private sealed class ProbeLayer : Layer
    {
        private readonly List<string>? m_calls;

        internal ProbeLayer(string name, List<string>? calls = null)
            : base(name)
        {
            m_calls = calls;
        }

        internal bool throwOnAttach { get; init; }

        internal bool throwOnRender { get; init; }

        internal bool throwOnDetach { get; init; }

        internal int pendingDetaches { get; set; }

        internal int detachCount { get; private set; }

        internal int eventCount { get; private set; }

        public override void OnAttach()
        {
            _ = Listen<ProbeEvent>(_ =>
            {
                eventCount++;
                m_calls?.Add(name);
            });
            if (throwOnAttach)
                throw new InvalidOperationException("Attach failed.");
        }

        public override void OnDetach()
        {
            detachCount++;
            if (pendingDetaches-- > 0)
                throw new RetirementPendingException("Expected unfinished layer work.");
            if (throwOnDetach)
                throw new InvalidOperationException("Detach failed.");
        }

        public override void OnBeforeRender(float deltaTime)
            => m_calls?.Add($"{name}.before");

        public override void OnRender(float deltaTime)
        {
            m_calls?.Add($"{name}.render");
            if (throwOnRender)
                throw new InvalidOperationException("Render failed.");
        }

        public override void OnAfterRender(float deltaTime)
            => m_calls?.Add($"{name}.after");
    }
}
