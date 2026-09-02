using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

using Inno.Core.Events;

namespace Inno.Editor.Application;

internal sealed class EditorHostLayerStack(Func<EventHub> eventHubFactory) : IDisposable
{
    private readonly Func<EventHub> m_eventHubFactory = eventHubFactory
        ?? throw new ArgumentNullException(nameof(eventHubFactory));
    private readonly List<Entry> m_layers = [];
    private int m_baseLayerCount;
    private bool m_disposed;

    internal void PushLayer(EditorHostLayer layer)
    {
        Insert(layer, m_baseLayerCount);
        m_baseLayerCount++;
    }

    internal void PushOverlay(EditorHostLayer layer)
        => Insert(layer, m_layers.Count);

    internal bool PopLayer(EditorHostLayer layer)
    {
        int index = Find(layer);
        if (index < 0 || index >= m_baseLayerCount)
            return false;
        m_baseLayerCount--;
        Remove(index);
        return true;
    }

    internal bool PopOverlay(EditorHostLayer layer)
    {
        int index = Find(layer);
        if (index < m_baseLayerCount)
            return false;
        Remove(index);
        return true;
    }

    internal void Update(float deltaTime)
    {
        EnsureActive();
        for (int index = 0; index < m_layers.Count; index++)
            m_layers[index].layer.Update(deltaTime);
    }

    internal void LateUpdate(float deltaTime)
    {
        EnsureActive();
        for (int index = 0; index < m_layers.Count; index++)
            m_layers[index].layer.LateUpdate(deltaTime);
    }

    internal void RenderFrame(float deltaTime)
    {
        EnsureActive();
        int preparedCount = 0;
        List<Exception>? failures = null;
        try
        {
            for (; preparedCount < m_layers.Count; preparedCount++)
                m_layers[preparedCount].layer.BeginRender(deltaTime);
            for (int index = 0; index < preparedCount; index++)
                m_layers[index].layer.Render(deltaTime);
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
                    m_layers[index].layer.EndRender(deltaTime);
                }
                catch (Exception exception)
                {
                    failures ??= [];
                    failures.Add(exception);
                }
            }
        }
        if (failures is null)
            return;
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException("The editor render frame encountered multiple failures.", failures);
    }

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        List<Exception>? failures = null;
        for (int index = m_layers.Count - 1; index >= 0; index--)
        {
            try
            {
                m_layers[index].layer.DetachFromHost();
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
            finally
            {
                m_layers[index].events.Dispose();
            }
        }
        m_layers.Clear();
        m_baseLayerCount = 0;
        if (failures is not null)
            throw new AggregateException("One or more editor host layers failed to detach.", failures);
    }

    private void Insert(EditorHostLayer layer, int index)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(layer);
        if (Find(layer) >= 0)
            throw new InvalidOperationException("The editor host layer is already attached.");
        EventHub events = m_eventHubFactory();
        m_layers.Insert(index, new Entry(layer, events));
        try
        {
            UpdateEventOrder();
            layer.AttachTo(events);
        }
        catch
        {
            m_layers.RemoveAt(index);
            events.Dispose();
            UpdateEventOrder();
            throw;
        }
    }

    private void Remove(int index)
    {
        EnsureActive();
        Entry entry = m_layers[index];
        m_layers.RemoveAt(index);
        try
        {
            entry.layer.DetachFromHost();
        }
        finally
        {
            entry.events.Dispose();
            UpdateEventOrder();
        }
    }

    private int Find(EditorHostLayer layer)
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
        => ObjectDisposedException.ThrowIf(m_disposed, this);

    private sealed record Entry(EditorHostLayer layer, EventHub events);
}
