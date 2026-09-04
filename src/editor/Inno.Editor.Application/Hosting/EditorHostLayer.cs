using System;
using System.Collections.Generic;

using Inno.Core.Events;

namespace Inno.Editor.Application;

internal abstract class EditorHostLayer
{
    private readonly List<IDisposable> m_subscriptions = [];
    private EventHub? m_events;

    internal virtual void Attach()
    {
    }

    internal virtual void Detach()
    {
    }

    internal virtual void Update(float deltaTime)
    {
    }

    internal virtual void LateUpdate(float deltaTime)
    {
    }

    internal virtual void BeginRender(float deltaTime)
    {
    }

    internal virtual void Render(float deltaTime)
    {
    }

    internal virtual void EndRender(float deltaTime)
    {
    }

    /// <summary>
    /// Subscribes the supplied handler and returns ownership of the subscription lifetime.
    /// </summary>
    /// <typeparam name="TEvent">
    /// The caller-selected tevent type whose declared constraints are enforced by this operation.
    /// </typeparam>
    /// <param name="handler">
    /// The callback invoked by listen within the operation's owned lifetime.
    /// </param>
    /// <param name="priority">
    /// The priority consumed by listen; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated idisposable that represents the completed operation.
    /// </returns>
    protected IDisposable Listen<TEvent>(Action<TEvent> handler, int priority = 0)
        where TEvent : Event
    {
        EventHub events = m_events
            ?? throw new InvalidOperationException("The editor host layer is not attached.");
        IDisposable subscription = events.Listen(handler, priority);
        m_subscriptions.Add(subscription);
        return subscription;
    }

    internal void AttachTo(EventHub events)
    {
        if (m_events is not null)
            throw new InvalidOperationException("The editor host layer is already attached.");
        m_events = events ?? throw new ArgumentNullException(nameof(events));
        try
        {
            Attach();
        }
        catch
        {
            m_events = null;
            throw;
        }
    }

    internal void DetachFromHost()
    {
        for (int index = m_subscriptions.Count - 1; index >= 0; index--)
            m_subscriptions[index].Dispose();
        m_subscriptions.Clear();
        try
        {
            Detach();
        }
        finally
        {
            m_events = null;
        }
    }
}
