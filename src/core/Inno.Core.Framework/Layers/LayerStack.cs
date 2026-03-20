using System;
using System.Collections.Generic;
using Inno.Core.Events;

namespace Inno.Core.Framework;

public sealed class LayerStack : IDisposable
{
    private readonly Func<EventHub> m_hubFactory;
    private readonly List<LayerEntry> m_layers = [];
    private int m_layerInsertIndex = 0;
    private bool m_disposed;

    public int count => m_layers.Count;
    public Layer this[int index] => m_layers[index].layer;

    public LayerStack(Func<EventHub> hubFactory)
    {
        m_hubFactory = hubFactory ?? throw new ArgumentNullException(nameof(hubFactory));
    }

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

    public void OnUpdate(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        foreach (var entry in m_layers)
        {
            entry.layer.OnUpdate(deltaTime);
        }
    }

    public void OnRender(float renderDeltaTime)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        foreach (var entry in m_layers)
        {
            entry.layer.OnRender(renderDeltaTime);
        }
    }

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
