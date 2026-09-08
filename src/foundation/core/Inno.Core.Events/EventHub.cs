using System;
using System.Collections.Generic;
using System.Threading;

namespace Inno.Core.Events;

/// <summary>
/// A disposable subscription hub bound to a single <see cref="EventDispatcher"/>.
/// </summary>
/// <remarks>
/// Listeners are ordered by priority (descending) and registration order (ascending).
/// </remarks>
public sealed class EventHub : IDisposable
{
    private readonly WeakReference<EventDispatcher> m_dispatcherRef;
    private readonly object m_gate = new();
    private HubState m_state = HubState.Empty;
    private long m_nextListenerOrder;
    private int m_disposed;
    private int m_attached = 1;
    private int m_order;

    internal EventHub(EventDispatcher dispatcher, int order, long sequence)
    {
        m_dispatcherRef = new WeakReference<EventDispatcher>(dispatcher);
        m_order = order;
        this.sequence = sequence;
    }

    /// <summary>
    /// Gets or sets the hub dispatch order. Higher values run earlier.
    /// </summary>
    public int order
    {
        get => m_order;
        set
        {
            if (m_order == value)
            {
                return;
            }

            m_order = value;
            if (m_dispatcherRef.TryGetTarget(out EventDispatcher? dispatcher))
            {
                dispatcher.NotifyHubOrderChanged();
            }
        }
    }

    /// <summary>
    /// Gets whether this hub is still attached to a live dispatcher and not disposed.
    /// </summary>
    public bool isValid
    {
        get
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return false;
            }

            return Volatile.Read(ref m_attached) != 0
                   && m_dispatcherRef.TryGetTarget(out _);
        }
    }

    internal long sequence { get; }

    /// <summary>
    /// Subscribes a listener for the specified event type.
    /// </summary>
    /// <typeparam name="TEvent">
    /// Event type to listen for.
    /// </typeparam>
    /// <param name="handler">
    /// Listener callback.
    /// </param>
    /// <param name="priority">
    /// Listener priority within this hub. Higher values run earlier.
    /// </param>
    /// <returns>
    /// A token that unsubscribes this listener when disposed.
    /// </returns>
    public IDisposable Listen<TEvent>(Action<TEvent> handler, int priority = 0)
        where TEvent : Event
    {
        ThrowIfInvalid();
        ArgumentNullException.ThrowIfNull(handler);

        Type eventType = typeof(TEvent);
        long listenerOrder;

        lock (m_gate)
        {
            ThrowIfInvalid();
            listenerOrder = m_nextListenerOrder++;

            Action<Event> invoker = e => handler((TEvent)e);
            Listener listener = new(invoker, priority, listenerOrder);

            Dictionary<Type, Listener[]> nextBuckets = new(Volatile.Read(ref m_state).buckets);
            Listener[] existing = nextBuckets.TryGetValue(eventType, out Listener[]? items) ? items : [];
            nextBuckets[eventType] = ListenerBucket.InsertSorted(existing, listener);
            Volatile.Write(ref m_state, new HubState(nextBuckets));
        }

        return new Subscription(this, eventType, listenerOrder);
    }

    /// <summary>
    /// Subscribes a one-shot listener for the specified event type.
    /// </summary>
    /// <typeparam name="TEvent">
    /// Event type to listen for.
    /// </typeparam>
    /// <param name="handler">
    /// Listener callback.
    /// </param>
    /// <param name="priority">
    /// Listener priority within this hub. Higher values run earlier.
    /// </param>
    /// <returns>
    /// A token that can cancel the one-shot listener before it runs.
    /// </returns>
    public IDisposable ListenOnce<TEvent>(Action<TEvent> handler, int priority = 0)
        where TEvent : Event
    {
        ThrowIfInvalid();
        ArgumentNullException.ThrowIfNull(handler);

        OneShotSubscription<TEvent> oneShot = new(handler);
        IDisposable subscription = Listen<TEvent>(oneShot.Invoke, priority);
        oneShot.Bind(subscription);
        return oneShot;
    }

    /// <summary>
    /// Immediately dispatches an event inside this hub only.
    /// </summary>
    /// <remarks>
    /// This does not route through the dispatcher hub chain.
    /// Only listeners registered on this hub are invoked.
    /// </remarks>
    /// <param name="e">
    /// The event instance to dispatch locally.
    /// </param>
    public void Announce(Event e)
    {
        ThrowIfInvalid();
        ArgumentNullException.ThrowIfNull(e);
        Dispatch(e);
    }

    /// <summary>
    /// Disposes this hub and removes all listeners in this layer.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref m_disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref m_attached, 0);

        lock (m_gate)
        {
            Volatile.Write(ref m_state, HubState.Empty);
        }

        if (m_dispatcherRef.TryGetTarget(out EventDispatcher? dispatcher))
        {
            dispatcher.RemoveHub(this);
        }
    }

    internal void Dispatch(Event e)
    {
        if (!isValid || e.isGlobalHandled)
        {
            return;
        }

        HubState snapshot = Volatile.Read(ref m_state);
        using Event.HubDispatchScope _ = e.BeginHubDispatchScope();

        Type? type = e.GetType();
        while (type is not null && typeof(Event).IsAssignableFrom(type))
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return;
            }

            if (!snapshot.buckets.TryGetValue(type, out Listener[]? listeners))
            {
                type = type.BaseType;
                continue;
            }

            for (int i = 0; i < listeners.Length; i++)
            {
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    return;
                }

                listeners[i].Invoke(e);
                if (e.isGlobalHandled || e.IsHandledInCurrentHub())
                {
                    return;
                }
            }

            type = type.BaseType;
        }
    }

    private void Unlisten(Type eventType, long listenerOrder)
    {
        if (Volatile.Read(ref m_disposed) != 0)
        {
            return;
        }

        lock (m_gate)
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return;
            }

            Dictionary<Type, Listener[]> nextBuckets = new(Volatile.Read(ref m_state).buckets);
            if (!nextBuckets.TryGetValue(eventType, out Listener[]? existing))
            {
                return;
            }

            Listener[] filtered = ListenerBucket.RemoveByOrder(existing, listenerOrder);
            if (filtered.Length == existing.Length)
            {
                return;
            }

            if (filtered.Length == 0)
            {
                nextBuckets.Remove(eventType);
            }
            else
            {
                nextBuckets[eventType] = filtered;
            }

            Volatile.Write(ref m_state, new HubState(nextBuckets));
        }
    }

    private void ThrowIfInvalid()
    {
        if (!isValid)
        {
            throw new InvalidOperationException("The EventHub is no longer valid.");
        }
    }

    private sealed class Subscription(EventHub hub, Type eventType, long listenerOrder) : IDisposable
    {
        private EventHub? m_hub = hub;

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            EventHub? hub = Interlocked.Exchange(ref m_hub, null);
            if (hub is null)
            {
                return;
            }

            hub.Unlisten(eventType, listenerOrder);
        }
    }

    private sealed class Listener(Action<Event> callback, int priority, long order)
    {
        /// <summary>
        /// Gets the scalar measurement or identity associated with the current state.
        /// </summary>
        public int priority { get; } = priority;
        /// <summary>
        /// Gets the scalar measurement or identity associated with the current state.
        /// </summary>
        public long order { get; } = order;

        /// <summary>
        /// Invokes the configured callback within this instance's ownership boundary.
        /// </summary>
        /// <param name="e">
        /// The e consumed by invoke; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        public void Invoke(Event e) => callback.Invoke(e);
    }

    private sealed class OneShotSubscription<TEvent>(Action<TEvent> handler) : IDisposable
        where TEvent : Event
    {
        private readonly Action<TEvent> m_handler = handler;
        private IDisposable? m_subscription;
        private int m_completed;

        /// <summary>
        /// Retains the supplied subscription until this owner is stopped or disposed.
        /// </summary>
        /// <param name="subscription">
        /// The subscription consumed by bind; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        public void Bind(IDisposable subscription)
        {
            if (Interlocked.CompareExchange(ref m_completed, 0, 0) != 0)
            {
                subscription.Dispose();
                return;
            }

            if (Interlocked.CompareExchange(ref m_subscription, subscription, null) is not null)
            {
                subscription.Dispose();
                return;
            }

            if (Interlocked.CompareExchange(ref m_completed, 0, 0) != 0)
            {
                DisposeBoundSubscription();
            }
        }

        /// <summary>
        /// Invokes the configured callback within this instance's ownership boundary.
        /// </summary>
        /// <param name="e">
        /// The e consumed by invoke; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        public void Invoke(TEvent e)
        {
            if (Interlocked.Exchange(ref m_completed, 1) != 0)
            {
                return;
            }

            DisposeBoundSubscription();
            m_handler.Invoke(e);
        }

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_completed, 1) != 0)
            {
                return;
            }

            DisposeBoundSubscription();
        }

        private void DisposeBoundSubscription()
        {
            IDisposable? subscription = Interlocked.Exchange(ref m_subscription, null);
            subscription?.Dispose();
        }
    }

    private sealed class ListenerBucket
    {
        /// <summary>
        /// Inserts a listener into the array while preserving deterministic priority order.
        /// </summary>
        /// <param name="source">
        /// The source value or location read by this operation.
        /// </param>
        /// <param name="listener">
        /// The listener consumed by insert sorted; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <returns>
        /// An immutable snapshot of the values selected by the operation.
        /// </returns>
        public static Listener[] InsertSorted(Listener[] source, Listener listener)
        {
            Listener[] result = new Listener[source.Length + 1];
            int index = 0;
            while (index < source.Length)
            {
                Listener current = source[index];
                if (listener.priority > current.priority)
                {
                    break;
                }

                if (listener.priority == current.priority && listener.order < current.order)
                {
                    break;
                }

                index++;
            }

            if (index > 0)
            {
                Array.Copy(source, 0, result, 0, index);
            }

            result[index] = listener;

            if (source.Length - index > 0)
            {
                Array.Copy(source, index, result, index + 1, source.Length - index);
            }

            return result;
        }

        /// <summary>
        /// Removes the listener with the requested registration order from the immutable array.
        /// </summary>
        /// <param name="source">
        /// The source value or location read by this operation.
        /// </param>
        /// <param name="listenerOrder">
        /// The listener order consumed by remove by order; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <returns>
        /// An immutable snapshot of the values selected by the operation.
        /// </returns>
        public static Listener[] RemoveByOrder(Listener[] source, long listenerOrder)
        {
            int index = -1;
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i].order != listenerOrder)
                {
                    continue;
                }

                index = i;
                break;
            }

            if (index < 0)
            {
                return source;
            }

            if (source.Length == 1)
            {
                return [];
            }

            Listener[] result = new Listener[source.Length - 1];
            if (index > 0)
            {
                Array.Copy(source, 0, result, 0, index);
            }

            if (source.Length - index - 1 > 0)
            {
                Array.Copy(source, index + 1, result, index, source.Length - index - 1);
            }

            return result;
        }
    }

    private sealed class HubState(Dictionary<Type, Listener[]> buckets)
    {
        /// <summary>
        /// The empty value used as part of this type's public representation.
        /// </summary>
        public static readonly HubState Empty = new(new Dictionary<Type, Listener[]>());
        /// <summary>
        /// Gets the immutable listener buckets captured for one dispatch operation.
        /// </summary>
        public Dictionary<Type, Listener[]> buckets { get; } = buckets;
    }
}
