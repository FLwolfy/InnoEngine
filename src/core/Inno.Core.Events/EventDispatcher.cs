using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Inno.Core.Events;

/// <summary>
/// Thread-safe event dispatcher that owns a set of <see cref="EventHub"/> instances
/// and routes events to hubs in descending <see cref="EventHub.order"/>.
/// </summary>
public sealed class EventDispatcher
{
    private readonly ConcurrentQueue<Event> m_queue = new();
    private readonly Lock m_hubsGate = new();
    private EventHub[] m_hubsSnapshot = [];
    private long m_nextHubSequence;

    /// <summary>
    /// Creates a new hub attached to this dispatcher.
    /// </summary>
    /// <param name="order">
    /// Hub dispatch order. Higher values run earlier.
    /// </param>
    /// <returns>The created valid hub.</returns>
    public EventHub CreateHub(int order = 0)
    {
        lock (m_hubsGate)
        {
            EventHub hub = new(this, order, m_nextHubSequence++);
            List<EventHub> hubs = [..m_hubsSnapshot, hub];
            SortHubs(hubs);
            Volatile.Write(ref m_hubsSnapshot, [..hubs]);
            return hub;
        }
    }

    /// <summary>
    /// Enqueues an event for later processing via <see cref="Flush"/>.
    /// </summary>
    /// <param name="e">The event instance to enqueue.</param>
    public void Enqueue(Event e)
    {
        ArgumentNullException.ThrowIfNull(e);
        m_queue.Enqueue(e);
    }

    /// <summary>
    /// Drains the current queue and dispatches each event using <see cref="Emit"/>.
    /// </summary>
    public void Flush()
    {
        while (m_queue.TryDequeue(out Event? e))
        {
            Emit(e);
        }
    }

    /// <summary>
    /// Immediately dispatches an event to all valid hubs in priority order.
    /// </summary>
    /// <remarks>
    /// Dispatch stops when the event is marked globally handled.
    /// </remarks>
    /// <param name="e">The event instance to dispatch.</param>
    public void Emit(Event e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.isGlobalHandled)
        {
            return;
        }

        EventHub[] hubs = Volatile.Read(ref m_hubsSnapshot);
        for (int i = 0; i < hubs.Length; i++)
        {
            EventHub hub = hubs[i];
            if (!hub.isValid)
            {
                continue;
            }

            hub.Dispatch(e);
            if (e.isGlobalHandled)
            {
                break;
            }
        }
    }

    internal void RemoveHub(EventHub hub)
    {
        lock (m_hubsGate)
        {
            List<EventHub> hubs = [..m_hubsSnapshot];
            hubs.Remove(hub);
            SortHubs(hubs);
            Volatile.Write(ref m_hubsSnapshot, [..hubs]);
        }
    }

    internal void NotifyHubOrderChanged()
    {
        lock (m_hubsGate)
        {
            List<EventHub> hubs = [..m_hubsSnapshot];
            SortHubs(hubs);
            Volatile.Write(ref m_hubsSnapshot, [..hubs]);
        }
    }

    private static void SortHubs(List<EventHub> hubs)
    {
        hubs.Sort(static (a, b) =>
        {
            int byOrder = b.order.CompareTo(a.order);
            if (byOrder != 0)
            {
                return byOrder;
            }

            return a.sequence.CompareTo(b.sequence);
        });
    }
}
