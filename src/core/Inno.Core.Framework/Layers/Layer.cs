using System;
using System.Collections.Generic;
using Inno.Core.Events;

namespace Inno.Core.Framework;

public abstract class Layer(string name = "Layer")
{
    private EventHub? m_eventHub;
    private readonly List<IDisposable> m_subscriptions = [];

    public string name { get; } = name;

    public virtual void OnAttach() { }
    public virtual void OnDetach() { }
    public virtual void OnUpdate(float deltaTime) { }
    public virtual void OnRender(float renderDeltaTime) { }

    protected IDisposable Listen<TEvent>(Action<TEvent> handler, int priority = 0)
        where TEvent : Event
    {
        EventHub hub = m_eventHub ?? throw new InvalidOperationException("Layer is not attached to an EventHub.");
        IDisposable subscription = hub.Listen(handler, priority);
        m_subscriptions.Add(subscription);
        return subscription;
    }

    protected IDisposable ListenOnce<TEvent>(Action<TEvent> handler, int priority = 0)
        where TEvent : Event
    {
        EventHub hub = m_eventHub ?? throw new InvalidOperationException("Layer is not attached to an EventHub.");
        IDisposable subscription = hub.ListenOnce(handler, priority);
        m_subscriptions.Add(subscription);
        return subscription;
    }

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
