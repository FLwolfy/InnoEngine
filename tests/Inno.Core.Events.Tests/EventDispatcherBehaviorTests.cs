using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

using Inno.Core.Events;

using Xunit;

namespace Inno.Core.Events.Tests;

public sealed class EventDispatcherBehaviorTests
{
    [Fact]
    public void CreateHub_ProducesValidHub()
    {
        var dispatcher = new EventDispatcher();
        using EventHub hub = dispatcher.CreateHub();

        Assert.True(hub.isValid);
    }

    [Fact]
    public void DisposeHub_MakesHubInvalidAndPreventsFurtherListen()
    {
        var dispatcher = new EventDispatcher();
        EventHub hub = dispatcher.CreateHub();

        hub.Dispose();

        Assert.False(hub.isValid);
        Assert.Throws<InvalidOperationException>(() => hub.Listen<ProbeEvent>(_ => { }));
    }

    [Fact]
    public void ListenTokenDispose_UnsubscribesListener()
    {
        var dispatcher = new EventDispatcher();
        using EventHub hub = dispatcher.CreateHub();
        var count = 0;

        IDisposable token = hub.Listen<ProbeEvent>(_ => count++);
        dispatcher.Emit(new ProbeEvent(1));
        token.Dispose();
        dispatcher.Emit(new ProbeEvent(2));

        Assert.Equal(1, count);
    }

    [Fact]
    public void ListenTokenDispose_ConcurrentDispose_IsSafeAndIdempotent()
    {
        var dispatcher = new EventDispatcher();
        using EventHub hub = dispatcher.CreateHub();
        var count = 0;

        IDisposable token = hub.Listen<ProbeEvent>(_ => Interlocked.Increment(ref count));
        Parallel.For(0, 64, _ => token.Dispose());
        dispatcher.Emit(new ProbeEvent(42));

        Assert.Equal(0, count);
    }

    [Fact]
    public void ListenOnce_InvokesOnlyOneTime()
    {
        var dispatcher = new EventDispatcher();
        using EventHub hub = dispatcher.CreateHub();
        var count = 0;

        hub.ListenOnce<ProbeEvent>(_ => count++);

        dispatcher.Emit(new ProbeEvent(1));
        dispatcher.Emit(new ProbeEvent(2));

        Assert.Equal(1, count);
    }

    [Fact]
    public void ListenOnce_ConcurrentEmit_StillInvokesOnlyOneTime()
    {
        const int iterations = 10_000;
        var dispatcher = new EventDispatcher();
        using EventHub hub = dispatcher.CreateHub();
        var count = 0;

        hub.ListenOnce<ProbeEvent>(_ => Interlocked.Increment(ref count));
        Parallel.For(0, iterations, i => { dispatcher.Emit(new ProbeEvent(i)); });

        Assert.Equal(1, count);
    }

    [Fact]
    public void EnqueueAndFlush_DispatchesQueuedEvents()
    {
        var dispatcher = new EventDispatcher();
        using EventHub hub = dispatcher.CreateHub();
        var values = new List<int>();

        hub.Listen<ProbeEvent>(e => values.Add(e.value));

        dispatcher.Enqueue(new ProbeEvent(7));
        dispatcher.Enqueue(new ProbeEvent(9));
        dispatcher.Flush();

        Assert.Equal([7, 9], values);
    }

    [Fact]
    public void HubOrder_HigherOrderRunsFirst()
    {
        var dispatcher = new EventDispatcher();
        using EventHub lowHub = dispatcher.CreateHub(order: 0);
        using EventHub highHub = dispatcher.CreateHub(order: 10);
        var order = new List<string>();

        lowHub.Listen<ProbeEvent>(_ => order.Add("low"));
        highHub.Listen<ProbeEvent>(_ => order.Add("high"));

        dispatcher.Emit(new ProbeEvent(1));

        Assert.Equal(["high", "low"], order);
    }

    [Fact]
    public void HandledStopsPropagation_WithinHubAndAcrossHubs()
    {
        var dispatcher = new EventDispatcher();
        using EventHub firstHub = dispatcher.CreateHub(order: 10);
        using EventHub secondHub = dispatcher.CreateHub(order: 0);
        var firstHubSecondListenerCalled = false;
        var secondHubCalled = false;

        firstHub.Listen<ProbeEvent>(e => e.HandleInGlobal());
        firstHub.Listen<ProbeEvent>(_ => firstHubSecondListenerCalled = true);
        secondHub.Listen<ProbeEvent>(_ => secondHubCalled = true);

        dispatcher.Emit(new ProbeEvent(3));

        Assert.False(firstHubSecondListenerCalled);
        Assert.False(secondHubCalled);
    }

    [Fact]
    public void HandledEventBeforeEmit_SkipsDispatch()
    {
        var dispatcher = new EventDispatcher();
        using EventHub hub = dispatcher.CreateHub();
        var called = false;

        hub.Listen<ProbeEvent>(_ => called = true);

        var ev = new ProbeEvent(5);
        ev.HandleInGlobal();
        dispatcher.Emit(ev);

        Assert.False(called);
    }

    [Fact]
    public void HandleInHub_StopsOnlyCurrentHub()
    {
        var dispatcher = new EventDispatcher();
        using EventHub firstHub = dispatcher.CreateHub(order: 10);
        using EventHub secondHub = dispatcher.CreateHub(order: 0);
        var firstHubSecondListenerCalled = false;
        var secondHubCalled = false;

        firstHub.Listen<ProbeEvent>(e => e.HandleInHub());
        firstHub.Listen<ProbeEvent>(_ => firstHubSecondListenerCalled = true);
        secondHub.Listen<ProbeEvent>(_ => secondHubCalled = true);

        dispatcher.Emit(new ProbeEvent(10));

        Assert.False(firstHubSecondListenerCalled);
        Assert.True(secondHubCalled);
    }

    [Fact]
    public void HandleInHub_DoesNotAffectNextHub()
    {
        var dispatcher = new EventDispatcher();
        using EventHub firstHub = dispatcher.CreateHub(order: 10);
        using EventHub secondHub = dispatcher.CreateHub(order: 0);
        var secondHubFirstListenerCalled = false;
        var secondHubSecondListenerCalled = false;

        firstHub.Listen<ProbeEvent>(e => e.HandleInHub());
        secondHub.Listen<ProbeEvent>(_ => secondHubFirstListenerCalled = true);
        secondHub.Listen<ProbeEvent>(_ => secondHubSecondListenerCalled = true);

        dispatcher.Emit(new ProbeEvent(11));

        Assert.True(secondHubFirstListenerCalled);
        Assert.True(secondHubSecondListenerCalled);
    }

    [Fact]
    public void ConcurrentEmit_AcrossThreads_IsStable()
    {
        const int iterations = 20_000;
        var dispatcher = new EventDispatcher();
        using EventHub hubA = dispatcher.CreateHub(order: 10);
        using EventHub hubB = dispatcher.CreateHub(order: 0);
        var countA = 0;
        var countB = 0;

        hubA.Listen<ProbeEvent>(_ => Interlocked.Increment(ref countA));
        hubB.Listen<ProbeEvent>(_ => Interlocked.Increment(ref countB));

        Parallel.For(0, iterations, i => { dispatcher.Emit(new ProbeEvent(i)); });

        Assert.Equal(iterations, countA);
        Assert.Equal(iterations, countB);
    }

    [Fact]
    public void ConcurrentEmit_WithHandleInHub_StopsOnlyCurrentHub()
    {
        const int iterations = 20_000;
        var dispatcher = new EventDispatcher();
        using EventHub hubA = dispatcher.CreateHub(order: 10);
        using EventHub hubB = dispatcher.CreateHub(order: 0);
        var hubAFirst = 0;
        var hubASecond = 0;
        var hubBCount = 0;

        hubA.Listen<ProbeEvent>(e =>
        {
            Interlocked.Increment(ref hubAFirst);
            if ((e.value & 1) == 0)
            {
                e.HandleInHub();
            }
        });
        hubA.Listen<ProbeEvent>(_ => Interlocked.Increment(ref hubASecond));
        hubB.Listen<ProbeEvent>(_ => Interlocked.Increment(ref hubBCount));

        Parallel.For(0, iterations, i => { dispatcher.Emit(new ProbeEvent(i)); });

        Assert.Equal(iterations, hubAFirst);
        Assert.Equal(iterations / 2, hubASecond);
        Assert.Equal(iterations, hubBCount);
    }

    [Fact]
    public void ConcurrentEmit_WithHandleInGlobal_StopsFollowingHubs()
    {
        const int iterations = 20_000;
        var dispatcher = new EventDispatcher();
        using EventHub hubA = dispatcher.CreateHub(order: 10);
        using EventHub hubB = dispatcher.CreateHub(order: 0);
        var hubAFirst = 0;
        var hubASecond = 0;
        var hubBCount = 0;

        hubA.Listen<ProbeEvent>(e =>
        {
            Interlocked.Increment(ref hubAFirst);
            if ((e.value & 1) == 0)
            {
                e.HandleInGlobal();
            }
        });
        hubA.Listen<ProbeEvent>(_ => Interlocked.Increment(ref hubASecond));
        hubB.Listen<ProbeEvent>(_ => Interlocked.Increment(ref hubBCount));

        Parallel.For(0, iterations, i => { dispatcher.Emit(new ProbeEvent(i)); });

        Assert.Equal(iterations, hubAFirst);
        Assert.Equal(iterations / 2, hubASecond);
        Assert.Equal(iterations / 2, hubBCount);
    }

    [Fact]
    public void Announce_DispatchesImmediately_OnlyInCurrentHub()
    {
        var dispatcher = new EventDispatcher();
        using EventHub hubA = dispatcher.CreateHub(order: 10);
        using EventHub hubB = dispatcher.CreateHub(order: 0);
        var hubACalled = false;
        var hubBCalled = false;

        hubA.Listen<ProbeEvent>(_ => hubACalled = true);
        hubB.Listen<ProbeEvent>(_ => hubBCalled = true);

        hubA.Announce(new ProbeEvent(1));

        Assert.True(hubACalled);
        Assert.False(hubBCalled);
    }

    [Fact]
    public void Announce_RespectsHandleInHub_InSameHub()
    {
        var dispatcher = new EventDispatcher();
        using EventHub hub = dispatcher.CreateHub();
        var secondCalled = false;

        hub.Listen<ProbeEvent>(e => e.HandleInHub());
        hub.Listen<ProbeEvent>(_ => secondCalled = true);

        hub.Announce(new ProbeEvent(2));

        Assert.False(secondCalled);
    }

    [Fact]
    public void DisposeHub_DuringDispatch_StopsRemainingListenersInSameHub()
    {
        var dispatcher = new EventDispatcher();
        EventHub hub = dispatcher.CreateHub();
        var secondCalled = false;

        hub.Listen<ProbeEvent>(_ => hub.Dispose());
        hub.Listen<ProbeEvent>(_ => secondCalled = true);

        dispatcher.Emit(new ProbeEvent(100));

        Assert.False(secondCalled);
        Assert.False(hub.isValid);
    }

    [Fact]
    public void DisposeHub_DuringAnnounce_StopsRemainingListenersInSameHub()
    {
        var dispatcher = new EventDispatcher();
        EventHub hub = dispatcher.CreateHub();
        var secondCalled = false;

        hub.Listen<ProbeEvent>(_ => hub.Dispose());
        hub.Listen<ProbeEvent>(_ => secondCalled = true);

        hub.Announce(new ProbeEvent(101));

        Assert.False(secondCalled);
        Assert.False(hub.isValid);
    }

    private sealed class ProbeEvent(int value) : Event
    {
        public int value { get; } = value;
    }
}
