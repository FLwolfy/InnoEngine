using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

using Inno.Core.Events;
using Inno.Core.Execution;

namespace Inno.Core.Layers;

/// <summary>
/// Owns an ordered collection of base layers and overlays with isolated event scopes.
/// </summary>
public sealed class LayerStack : IDisposable
{
    private readonly Func<EventHub> m_eventHubFactory;
    private readonly List<Entry> m_layers = [];
    private int m_baseLayerCount;
    private bool m_disposed;
    private bool m_stopping;
    private List<Exception>? m_retirementFailures;

    /// <summary>
    /// Creates a layer stack that obtains one event scope for each attachment.
    /// </summary>
    /// <param name="eventHubFactory">
    /// The factory used to create a distinct event hub for every attached layer.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="eventHubFactory"/> is <see langword="null"/>.
    /// </exception>
    public LayerStack(Func<EventHub> eventHubFactory)
    {
        m_eventHubFactory = eventHubFactory ?? throw new ArgumentNullException(nameof(eventHubFactory));
    }

    /// <summary>
    /// Gets the number of attached base layers and overlays.
    /// </summary>
    public int count => m_layers.Count;

    /// <summary>
    /// Gets the layer at the supplied stack position.
    /// </summary>
    /// <param name="index">
    /// The zero-based stack position.
    /// </param>
    /// <returns>
    /// The attached layer at the requested position.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="index"/> is outside the current stack.
    /// </exception>
    public Layer this[int index] => m_layers[index].layer;

    /// <summary>
    /// Attaches a base layer below all overlays.
    /// </summary>
    /// <param name="layer">
    /// The layer to attach.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="layer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the same instance is already attached or the event hub factory returns <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this stack has been disposed.
    /// </exception>
    public void PushLayer(Layer layer)
    {
        Insert(layer, m_baseLayerCount, isBaseLayer: true);
    }

    /// <summary>
    /// Attaches an overlay above all base layers and existing overlays.
    /// </summary>
    /// <param name="overlay">
    /// The overlay to attach.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="overlay"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the same instance is already attached or the event hub factory returns <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this stack has been disposed.
    /// </exception>
    public void PushOverlay(Layer overlay)
        => Insert(overlay, m_layers.Count, isBaseLayer: false);

    /// <summary>
    /// Detaches a base layer from this stack.
    /// </summary>
    /// <param name="layer">
    /// The base layer to detach.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the layer was attached in the base region; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="layer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this stack has been disposed.
    /// </exception>
    public bool PopLayer(Layer layer)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(layer);
        int index = Find(layer);
        if (index < 0 || index >= m_baseLayerCount)
            return false;
        Remove(index);
        return true;
    }

    /// <summary>
    /// Detaches an overlay from this stack.
    /// </summary>
    /// <param name="overlay">
    /// The overlay to detach.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the layer was attached in the overlay region; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="overlay"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this stack has been disposed.
    /// </exception>
    public bool PopOverlay(Layer overlay)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(overlay);
        int index = Find(overlay);
        if (index < m_baseLayerCount)
            return false;
        Remove(index);
        return true;
    }

    /// <summary>
    /// Advances fixed-step callbacks in bottom-to-top stack order.
    /// </summary>
    /// <param name="fixedDeltaTime">
    /// The fixed simulation interval in seconds.
    /// </param>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this stack has been disposed.
    /// </exception>
    public void OnFixedUpdate(float fixedDeltaTime)
    {
        EnsureActive();
        for (int index = 0; index < m_layers.Count; index++)
            m_layers[index].layer.OnFixedUpdate(fixedDeltaTime);
    }

    /// <summary>
    /// Advances variable-step callbacks in bottom-to-top stack order.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this stack has been disposed.
    /// </exception>
    public void OnUpdate(float deltaTime)
    {
        EnsureActive();
        for (int index = 0; index < m_layers.Count; index++)
            m_layers[index].layer.OnUpdate(deltaTime);
    }

    /// <summary>
    /// Advances late-update callbacks in bottom-to-top stack order.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this stack has been disposed.
    /// </exception>
    public void OnLateUpdate(float deltaTime)
    {
        EnsureActive();
        for (int index = 0; index < m_layers.Count; index++)
            m_layers[index].layer.OnLateUpdate(deltaTime);
    }

    /// <summary>
    /// Executes preparation and submission in stack order, then completion in reverse order.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    /// <exception cref="AggregateException">
    /// Thrown when multiple render or unwind callbacks fail.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this stack has been disposed.
    /// </exception>
    public void RenderFrame(float deltaTime)
    {
        EnsureActive();
        int preparedCount = 0;
        List<Exception>? failures = null;
        try
        {
            for (; preparedCount < m_layers.Count; preparedCount++)
                m_layers[preparedCount].layer.OnBeforeRender(deltaTime);
            for (int index = 0; index < preparedCount; index++)
                m_layers[index].layer.OnRender(deltaTime);
        }
        catch (Exception exception)
        {
            failures = [exception];
        }
        finally
        {
            for (int index = preparedCount - 1; index >= 0; index--)
            {
                try
                {
                    m_layers[index].layer.OnAfterRender(deltaTime);
                }
                catch (Exception exception)
                {
                    failures ??= [];
                    failures.Add(exception);
                }
            }
        }

        ThrowFailures(failures, "One or more layer render callbacks failed.");
    }

    /// <summary>
    /// Detaches every layer in top-to-bottom order while keeping this stack reusable.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// Cleanup is incomplete. The remaining layers stay owned; only Clear or Dispose may resume cleanup.
    /// </exception>
    /// <exception cref="AggregateException">
    /// Thrown after cleanup completes when multiple layers fail to detach.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this stack has been disposed.
    /// </exception>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        List<Exception>? failures = DetachAll();
        m_stopping = false;
        ThrowFailures(failures, "One or more layers failed to detach.");
    }

    /// <summary>
    /// Detaches every layer and permanently releases this stack.
    /// </summary>
    /// <exception cref="RetirementPendingException">
    /// A layer still owns live work. The current and lower layers remain owned until disposal is retried.
    /// </exception>
    /// <exception cref="AggregateException">
    /// Thrown after cleanup completes when multiple layers fail to detach.
    /// </exception>
    public void Dispose()
    {
        if (m_disposed)
            return;
        List<Exception>? failures = DetachAll();
        m_disposed = true;
        ThrowFailures(failures, "One or more layers failed to detach while disposing the stack.");
    }

    private void Insert(Layer layer, int index, bool isBaseLayer)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(layer);
        if (layer.isAttached || Find(layer) >= 0)
            throw new InvalidOperationException($"Layer '{layer.name}' is already attached to this stack.");

        EventHub events = m_eventHubFactory()
            ?? throw new InvalidOperationException("The layer event hub factory returned null.");
        m_layers.Insert(index, new Entry(layer, events));
        if (isBaseLayer)
            m_baseLayerCount++;
        try
        {
            UpdateEventOrder();
            layer.Attach(events);
        }
        catch (Exception attachFailure)
        {
            try
            {
                Remove(index);
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null)
            {
                m_retirementFailures ??= [];
                m_retirementFailures.Add(attachFailure);
                throw;
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Layer attachment and compensation failed.", attachFailure, cleanupFailure);
            }
            throw;
        }
    }

    private void Remove(int index)
    {
        Entry entry = m_layers[index];
        List<Exception>? failures = null;
        try
        {
            if (entry.layer.isAttached)
                entry.layer.Detach();
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null)
        {
            m_stopping = true;
            throw;
        }
        catch (Exception exception)
        {
            failures = [exception];
        }
        try { entry.events.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        m_layers.RemoveAt(index);
        if (index < m_baseLayerCount)
            m_baseLayerCount--;
        UpdateEventOrder();
        ThrowFailures(failures, "Layer detachment and event-scope retirement failed.");
    }

    private List<Exception>? DetachAll()
    {
        m_stopping = true;
        while (m_layers.Count > 0)
        {
            try
            {
                Remove(m_layers.Count - 1);
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null)
            {
                throw;
            }
            catch (Exception exception)
            {
                (m_retirementFailures ??= []).Add(exception);
            }
        }
        List<Exception>? failures = m_retirementFailures;
        m_retirementFailures = null;
        return failures;
    }

    private int Find(Layer layer)
    {
        for (int index = 0; index < m_layers.Count; index++)
        {
            if (ReferenceEquals(m_layers[index].layer, layer))
                return index;
        }
        return -1;
    }

    private void UpdateEventOrder()
    {
        for (int index = 0; index < m_layers.Count; index++)
            m_layers[index].events.order = index;
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_stopping)
            throw new InvalidOperationException("Layer retirement is pending; resume Clear or Dispose before using this stack.");
    }

    private static void ThrowFailures(List<Exception>? failures, string message)
    {
        if (failures is null)
            return;
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException(message, failures);
    }

    private sealed record Entry(Layer layer, EventHub events);
}
