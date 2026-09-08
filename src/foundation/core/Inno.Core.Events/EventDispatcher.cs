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
    private readonly int m_queueCapacity;
    private readonly int m_flushBudget;
    private int m_pendingCount;

    /// <summary>
    /// Creates a dispatcher with bounded pending work and a finite per-flush budget.
    /// </summary>
    /// <param name="queueCapacity">
    /// Maximum pending events; producers must handle explicit rejection.
    /// </param>
    /// <param name="flushBudget">
    /// Maximum events dispatched by one flush, excluding events enqueued during that flush.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A budget is not positive.
    /// </exception>
    public EventDispatcher(int queueCapacity = 65536, int flushBudget = 4096)
    {
        if (queueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        if (flushBudget <= 0)
            throw new ArgumentOutOfRangeException(nameof(flushBudget));
        m_queueCapacity = queueCapacity;
        m_flushBudget = flushBudget;
    }

    /// <summary>
    /// Gets the number of pending events, including producer reservations.
    /// </summary>
    public int pendingCount => Volatile.Read(ref m_pendingCount);

    /// <summary>
    /// Creates a new hub attached to this dispatcher.
    /// </summary>
    /// <param name="order">
    /// Hub dispatch order. Higher values run earlier.
    /// </param>
    /// <returns>
    /// The created valid hub.
    /// </returns>
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
    /// <param name="e">
    /// The event instance to enqueue.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The queue is full; the event was not accepted.
    /// </exception>
    public void Enqueue(Event e)
    {
        if (!TryEnqueue(e))
            throw new InvalidOperationException("The event queue is full; the producer must defer or reject this operation explicitly.");
    }

    /// <summary>
    /// Attempts to enqueue without silently dropping a critical event on overload.
    /// </summary>
    /// <param name="e">
    /// The event whose ownership transfers only on success.
    /// </param>
    /// <returns>
    /// False when capacity is exhausted; the caller retains the event.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The event is null.
    /// </exception>
    public bool TryEnqueue(Event e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (Interlocked.Increment(ref m_pendingCount) > m_queueCapacity)
        {
            Interlocked.Decrement(ref m_pendingCount);
            return false;
        }
        m_queue.Enqueue(e);
        return true;
    }

    /// <summary>
    /// Dispatches a bounded prefix of pending events; recursively enqueued work waits for a later flush.
    /// </summary>
    public void Flush()
    {
        int remaining = Math.Min(m_queue.Count, m_flushBudget);
        while (remaining-- > 0 && m_queue.TryDequeue(out Event? e))
        {
            Interlocked.Decrement(ref m_pendingCount);
            Emit(e);
        }
    }

    /// <summary>
    /// Releases queued references at a quiescent owner safe point without invoking retired handlers.
    /// </summary>
    /// <returns>
    /// The number of pending events discarded by the owner.
    /// </returns>
    public int DiscardPending()
    {
        int count = 0;
        while (m_queue.TryDequeue(out _))
        {
            Interlocked.Decrement(ref m_pendingCount);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Immediately dispatches an event to all valid hubs in priority order.
    /// </summary>
    /// <remarks>
    /// Dispatch stops when the event is marked globally handled.
    /// </remarks>
    /// <param name="e">
    /// The event instance to dispatch.
    /// </param>
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
