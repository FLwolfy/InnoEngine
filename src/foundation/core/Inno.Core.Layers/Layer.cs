using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

using Inno.Core.Events;
using Inno.Core.Execution;

namespace Inno.Core.Layers;

/// <summary>
/// Defines a backend-neutral lifecycle participant that can receive frame callbacks and own event subscriptions.
/// </summary>
public abstract class Layer
{
    private readonly List<IDisposable> m_subscriptions = [];
    private EventHub? m_events;
    private List<Exception>? m_detachFailures;
    private bool m_detaching;

    /// <summary>
    /// Creates a layer with the supplied diagnostic name.
    /// </summary>
    /// <param name="name">
    /// The non-empty name used to identify the layer in diagnostics.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is empty or consists only of whitespace.
    /// </exception>
    protected Layer(string name = "Layer")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A layer name is required.", nameof(name));
        this.name = name;
    }

    /// <summary>
    /// Gets the diagnostic name of this layer.
    /// </summary>
    public string name { get; }

    /// <summary>
    /// Runs after the layer has received its event scope and joined a stack.
    /// </summary>
    public virtual void OnAttach()
    {
    }

    /// <summary>
    /// Runs while the layer is leaving its stack and before its event scope is released.
    /// </summary>
    /// <remarks>
    /// This callback also compensates a partially failed attachment. Throwing
    /// <see cref="RetirementPendingException"/> retains ownership and permits a later cleanup retry.
    /// Completed cleanup steps must not be repeated on that retry.
    /// </remarks>
    public virtual void OnDetach()
    {
    }

    /// <summary>
    /// Advances fixed-step work for this layer.
    /// </summary>
    /// <param name="fixedDeltaTime">
    /// The fixed simulation interval in seconds.
    /// </param>
    public virtual void OnFixedUpdate(float fixedDeltaTime)
    {
    }

    /// <summary>
    /// Advances variable-step work for this layer.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    public virtual void OnUpdate(float deltaTime)
    {
    }

    /// <summary>
    /// Advances work that must run after the regular update phase.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    public virtual void OnLateUpdate(float deltaTime)
    {
    }

    /// <summary>
    /// Prepares frame-scoped rendering state before render work is submitted.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    public virtual void OnBeforeRender(float deltaTime)
    {
    }

    /// <summary>
    /// Submits rendering work without presenting a concrete graphics backend.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    public virtual void OnRender(float deltaTime)
    {
    }

    /// <summary>
    /// Completes frame-scoped rendering state after render submission.
    /// </summary>
    /// <remarks>
    /// A <see cref="LayerStack"/> invokes this callback in reverse order for every layer whose
    /// <see cref="OnBeforeRender"/> callback completed successfully, including when later work fails.
    /// </remarks>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    public virtual void OnAfterRender(float deltaTime)
    {
    }

    /// <summary>
    /// Subscribes to an event for the lifetime of this layer attachment.
    /// </summary>
    /// <typeparam name="TEvent">
    /// The event type handled by the callback.
    /// </typeparam>
    /// <param name="handler">
    /// The callback invoked when the event reaches this layer.
    /// </param>
    /// <param name="priority">
    /// The listener priority inside this layer. Higher values run earlier.
    /// </param>
    /// <returns>
    /// A token that can remove the subscription before the layer detaches.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the layer is not currently attached to a stack.
    /// </exception>
    protected IDisposable Listen<TEvent>(Action<TEvent> handler, int priority = 0)
        where TEvent : Event
    {
        EventHub events = RequireActiveEvents();
        IDisposable subscription = events.Listen(handler, priority);
        m_subscriptions.Add(subscription);
        return subscription;
    }

    /// <summary>
    /// Subscribes once to an event for the lifetime of this layer attachment.
    /// </summary>
    /// <typeparam name="TEvent">
    /// The event type handled by the callback.
    /// </typeparam>
    /// <param name="handler">
    /// The callback invoked at most once when the event reaches this layer.
    /// </param>
    /// <param name="priority">
    /// The listener priority inside this layer. Higher values run earlier.
    /// </param>
    /// <returns>
    /// A token that can cancel the one-shot subscription before it runs.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the layer is not currently attached to a stack.
    /// </exception>
    protected IDisposable ListenOnce<TEvent>(Action<TEvent> handler, int priority = 0)
        where TEvent : Event
    {
        EventHub events = RequireActiveEvents();
        IDisposable subscription = events.ListenOnce(handler, priority);
        m_subscriptions.Add(subscription);
        return subscription;
    }

    /// <summary>
    /// Dispatches an event immediately inside this layer's event scope.
    /// </summary>
    /// <param name="evnt">
    /// The event to dispatch to this layer's listeners.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the layer is not currently attached to a stack.
    /// </exception>
    protected void Announce(Event evnt)
    {
        ArgumentNullException.ThrowIfNull(evnt);
        EventHub events = RequireActiveEvents();
        events.Announce(evnt);
    }

    internal bool isAttached => m_events is not null;

    internal void Attach(EventHub events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (m_events is not null)
            throw new InvalidOperationException($"Layer '{name}' is already attached.");

        m_events = events;
        OnAttach();
    }

    internal void Detach()
    {
        if (m_events is null)
            throw new InvalidOperationException($"Layer '{name}' is not attached.");

        if (!m_detaching)
        {
            m_detaching = true;
            m_detachFailures = ReleaseSubscriptions();
        }
        try
        {
            OnDetach();
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null)
        {
            throw;
        }
        catch (Exception exception)
        {
            m_detachFailures ??= [];
            m_detachFailures.Add(exception);
        }

        m_events = null;
        m_detaching = false;
        List<Exception>? failures = m_detachFailures;
        m_detachFailures = null;
        if (failures is null)
            return;
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException($"Layer '{name}' encountered multiple detach failures.", failures);
    }

    private EventHub RequireActiveEvents()
        => !m_detaching && m_events is not null
            ? m_events
            : throw new InvalidOperationException("The layer has no active event scope or is already detaching.");

    private List<Exception>? ReleaseSubscriptions()
    {
        List<Exception>? failures = null;
        for (int index = m_subscriptions.Count - 1; index >= 0; index--)
        {
            try
            {
                m_subscriptions[index].Dispose();
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }
        m_subscriptions.Clear();
        return failures;
    }
}
