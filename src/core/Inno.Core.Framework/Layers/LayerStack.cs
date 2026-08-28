using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Inno.Core.Events;

namespace Inno.Core.Framework;

/// <summary>
/// Ordered container of layers and overlays with per-layer event hubs.
/// </summary>
public sealed class LayerStack : IDisposable
{
    private readonly Func<EventHub> m_hubFactory;
    private readonly List<LayerEntry> m_layers = [];
    private int m_layerInsertIndex = 0;
    private bool m_disposed;

    /// <summary>
    /// Gets the number of attached layers and overlays.
    /// </summary>
    public int count => m_layers.Count;

    /// <summary>
    /// Gets the layer at the specified index.
    /// </summary>
    /// <param name="index">Layer index.</param>
    /// <returns>Layer at the given index.</returns>
    public Layer this[int index] => m_layers[index].layer;

    /// <summary>
    /// Creates a layer stack.
    /// </summary>
    /// <param name="hubFactory">Factory used to create a new <see cref="EventHub"/> per layer.</param>
    public LayerStack(Func<EventHub> hubFactory)
    {
        m_hubFactory = hubFactory ?? throw new ArgumentNullException(nameof(hubFactory));
    }

    /// <summary>
    /// Adds a base layer below overlays.
    /// </summary>
    /// <param name="layer">Layer instance.</param>
    public void PushLayer(Layer layer)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(layer);
        EnsureNotAttached(layer);

        EventHub hub = m_hubFactory.Invoke();
        LayerEntry entry = new(layer, hub);
        m_layers.Insert(m_layerInsertIndex, entry);
        m_layerInsertIndex++;

        try
        {
            UpdateHubOrders();
            layer.Attach(hub);
        }
        catch
        {
            m_layerInsertIndex--;
            m_layers.RemoveAt(m_layerInsertIndex);
            hub.Dispose();
            UpdateHubOrders();
            throw;
        }
    }

    /// <summary>
    /// Adds an overlay layer on top of all base layers.
    /// </summary>
    /// <param name="overlay">Overlay instance.</param>
    public void PushOverlay(Layer overlay)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(overlay);
        EnsureNotAttached(overlay);

        EventHub hub = m_hubFactory.Invoke();
        LayerEntry entry = new(overlay, hub);
        m_layers.Add(entry);

        try
        {
            UpdateHubOrders();
            overlay.Attach(hub);
        }
        catch
        {
            m_layers.RemoveAt(m_layers.Count - 1);
            hub.Dispose();
            UpdateHubOrders();
            throw;
        }
    }

    /// <summary>
    /// Removes a base layer.
    /// </summary>
    /// <param name="layer">Layer to remove.</param>
    /// <returns><see langword="true"/> when removed; otherwise <see langword="false"/>.</returns>
    public bool PopLayer(Layer layer)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(layer);

        int index = FindLayerIndex(layer);
        if (index < 0 || index >= m_layerInsertIndex)
        {
            return false;
        }

        LayerEntry entry = m_layers[index];
        m_layers.RemoveAt(index);
        m_layerInsertIndex--;
        try
        {
            entry.layer.Detach();
        }
        finally
        {
            entry.hub.Dispose();
        }

        UpdateHubOrders();
        return true;
    }

    /// <summary>
    /// Removes an overlay layer.
    /// </summary>
    /// <param name="overlay">Overlay to remove.</param>
    /// <returns><see langword="true"/> when removed; otherwise <see langword="false"/>.</returns>
    public bool PopOverlay(Layer overlay)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(overlay);

        int index = FindLayerIndex(overlay);
        if (index < 0 || index < m_layerInsertIndex)
        {
            return false;
        }

        LayerEntry entry = m_layers[index];
        m_layers.RemoveAt(index);
        try
        {
            entry.layer.Detach();
        }
        finally
        {
            entry.hub.Dispose();
        }

        UpdateHubOrders();
        return true;
    }

    /// <summary>
    /// Executes per-frame update on all layers in stack order.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public void OnUpdate(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        foreach (var entry in m_layers)
        {
            entry.layer.OnUpdate(deltaTime);
        }
    }

    /// <summary>
    /// Executes fixed-step updates on all layers in stack order.
    /// </summary>
    /// <param name="fixedDeltaTime">Fixed timestep in seconds.</param>
    public void OnFixedUpdate(float fixedDeltaTime)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        foreach (var entry in m_layers)
        {
            entry.layer.OnFixedUpdate(fixedDeltaTime);
        }
    }

    /// <summary>
    /// Executes late-update callbacks on all layers in stack order.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public void OnLateUpdate(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        foreach (var entry in m_layers)
        {
            entry.layer.OnLateUpdate(deltaTime);
        }
    }

    /// <summary>
    /// Executes the complete render lifecycle for all attached layers.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    /// <remarks>
    /// Preparation and submission execute in stack order. Completion executes
    /// in reverse order and always unwinds every layer that completed preparation.
    /// </remarks>
    /// <exception cref="AggregateException">
    /// Thrown when more than one render callback fails during execution or unwind.
    /// </exception>
    public void RenderFrame(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        int preparedCount = 0;
        List<Exception>? errors = null;

        try
        {
            for (; preparedCount < m_layers.Count; preparedCount++)
            {
                m_layers[preparedCount].layer.OnBeforeRender(deltaTime);
            }

            for (int i = 0; i < preparedCount; i++)
            {
                m_layers[i].layer.OnRender(deltaTime);
            }
        }
        catch (Exception exception)
        {
            errors = [exception];
        }
        finally
        {
            for (int i = preparedCount - 1; i >= 0; i--)
            {
                try
                {
                    m_layers[i].layer.OnAfterRender(deltaTime);
                }
                catch (Exception exception)
                {
                    errors ??= [];
                    errors.Add(exception);
                }
            }
        }

        if (errors is null)
        {
            return;
        }

        if (errors.Count == 1)
        {
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        }

        throw new AggregateException("One or more layer render callbacks failed.", errors);
    }

    /// <summary>
    /// Detaches and removes all layers and overlays.
    /// </summary>
    public void Clear()
    {
        if (m_disposed)
        {
            return;
        }

        for (int i = m_layers.Count - 1; i >= 0; i--)
        {
            LayerEntry entry = m_layers[i];
            try
            {
                entry.layer.Detach();
            }
            finally
            {
                entry.hub.Dispose();
            }
        }

        m_layers.Clear();
        m_layerInsertIndex = 0;
    }

    /// <summary>
    /// Disposes the stack and detaches all layers.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        Clear();
        m_disposed = true;
    }

    private void EnsureNotAttached(Layer layer)
    {
        if (FindLayerIndex(layer) >= 0)
        {
            throw new InvalidOperationException($"Layer '{layer.name}' is already attached.");
        }
    }

    private void UpdateHubOrders()
    {
        for (int i = 0; i < m_layers.Count; i++)
        {
            m_layers[i].hub.order = i;
        }
    }

    private int FindLayerIndex(Layer layer)
    {
        for (int i = 0; i < m_layers.Count; i++)
        {
            if (ReferenceEquals(m_layers[i].layer, layer))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class LayerEntry(Layer layer, EventHub hub)
    {
        public Layer layer { get; } = layer;
        public EventHub hub { get; } = hub;
    }
}
