using System;
using System.Collections.Generic;
using Inno.Core.Events;

namespace Inno.Core.Framework;

/// <summary>
/// Base type for engine layers.
/// </summary>
public abstract class Layer(string name = "Layer")
{
    private EventHub? m_eventHub;
    private readonly List<IDisposable> m_subscriptions = [];

    /// <summary>
    /// Gets the layer display/debug name.
    /// </summary>
    public string name { get; } = name;

    /// <summary>
    /// Called when the layer is attached to a layer stack.
    /// </summary>
    public virtual void OnAttach() { }

    /// <summary>
    /// Called when the layer is detached from a layer stack.
    /// </summary>
    public virtual void OnDetach() { }

    /// <summary>
    /// Called at fixed simulation intervals.
    /// </summary>
    /// <param name="fixedDeltaTime">Fixed timestep in seconds.</param>
    public virtual void OnFixedUpdate(float fixedDeltaTime) { }

    /// <summary>
    /// Called once per frame for variable-step updates.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public virtual void OnUpdate(float deltaTime) { }

    /// <summary>
    /// Called when this layer should render.
    /// </summary>
    /// <param name="renderDeltaTime">Render delta time in seconds.</param>
    public virtual void OnRender(float renderDeltaTime) { }

    /// <summary>
    /// Subscribes to events in this layer's event hub.
    /// Subscription is automatically disposed when the layer detaches.
    /// </summary>
    /// <typeparam name="TEvent">Event type to listen for.</typeparam>
    /// <param name="handler">Event handler.</param>
    /// <param name="priority">Listener priority within this layer hub.</param>
    /// <returns>A disposable subscription token.</returns>
    protected IDisposable Listen<TEvent>(Action<TEvent> handler, int priority = 0)
        where TEvent : Event
    {
        EventHub hub = m_eventHub ?? throw new InvalidOperationException("Layer is not attached to an EventHub.");
        IDisposable subscription = hub.Listen(handler, priority);
        m_subscriptions.Add(subscription);
        return subscription;
    }

    /// <summary>
    /// Subscribes a one-shot listener in this layer's event hub.
    /// The listener automatically unsubscribes after the first invocation.
    /// </summary>
    /// <typeparam name="TEvent">Event type to listen for.</typeparam>
    /// <param name="handler">Event handler.</param>
    /// <param name="priority">Listener priority within this layer hub.</param>
    /// <returns>A disposable token that can cancel the subscription before it runs.</returns>
    protected IDisposable ListenOnce<TEvent>(Action<TEvent> handler, int priority = 0)
        where TEvent : Event
    {
        EventHub hub = m_eventHub ?? throw new InvalidOperationException("Layer is not attached to an EventHub.");
        IDisposable subscription = hub.ListenOnce(handler, priority);
        m_subscriptions.Add(subscription);
        return subscription;
    }

    /// <summary>
    /// Dispatches an event immediately to this layer's own event hub only.
    /// </summary>
    /// <param name="e">Event instance.</param>
    protected void Announce(Event e)
    {
        ArgumentNullException.ThrowIfNull(e);
        EventHub hub = m_eventHub ?? throw new InvalidOperationException("Layer is not attached to an EventHub.");
        hub.Announce(e);
    }

    internal void Attach(EventHub hub)
    {
        m_eventHub = hub ?? throw new ArgumentNullException(nameof(hub));
        OnAttach();
    }

    internal void Detach()
    {
        for (int i = m_subscriptions.Count - 1; i >= 0; i--)
        {
            m_subscriptions[i].Dispose();
        }

        m_subscriptions.Clear();

        try
        {
            OnDetach();
        }
        finally
        {
            m_eventHub = null;
        }
    }
}
