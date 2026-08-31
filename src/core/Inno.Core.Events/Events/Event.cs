using System;
using System.Threading;

namespace Inno.Core.Events;

/// <summary>
/// Base type for all engine events.
/// </summary>
public abstract class Event
{
    private int m_globalHandled;
    [ThreadStatic]
    private static HubFrameStack? t_hubFrames;

    /// <summary>
    /// Marks this event as globally handled.
    /// </summary>
    /// <remarks>
    /// Global handling stops dispatch in the current hub and all following hubs.
    /// </remarks>
    public void HandleInGlobal()
    {
        Volatile.Write(ref m_globalHandled, 1);
    }

    /// <summary>
    /// Marks this event as handled in the current hub only.
    /// </summary>
    /// <remarks>
    /// Hub handling stops remaining listeners in the current hub
    /// but does not affect following hubs.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when called outside a valid hub dispatch context for this event.
    /// </exception>
    public void HandleInHub()
    {
        HubFrameStack? frames = t_hubFrames;
        if (frames is null || !frames.IsCurrent(this))
        {
            throw new InvalidOperationException("HandleInHub can only be used while this event is being dispatched in a hub.");
        }

        frames.MarkHandledCurrent();
    }

    internal bool isGlobalHandled => Volatile.Read(ref m_globalHandled) == 1;

    internal HubDispatchScope BeginHubDispatchScope()
    {
        HubFrameStack? frames = t_hubFrames;
        if (frames is null)
        {
            frames = new HubFrameStack(4);
            t_hubFrames = frames;
        }

        frames.Push(this);
        return new HubDispatchScope(frames, this);
    }

    internal bool IsHandledInCurrentHub()
    {
        HubFrameStack? frames = t_hubFrames;
        return frames is not null && frames.IsCurrentHandled(this);
    }

    internal readonly struct HubDispatchScope(HubFrameStack frames, Event eventRef) : IDisposable
    {
        public void Dispose()
        {
            frames.PopIfCurrent(eventRef);
        }
    }

    internal sealed class HubFrameStack
    {
        private Event?[] m_events;
        private bool[] m_handled;
        private int m_count;

        public HubFrameStack(int capacity)
        {
            m_events = new Event[capacity];
            m_handled = new bool[capacity];
        }

        public void Push(Event e)
        {
            EnsureCapacity(m_count + 1);
            m_events[m_count] = e;
            m_handled[m_count] = false;
            m_count++;
        }

        public bool IsCurrent(Event e)
        {
            return m_count > 0 && ReferenceEquals(m_events[m_count - 1], e);
        }

        public void MarkHandledCurrent()
        {
            if (m_count > 0)
            {
                m_handled[m_count - 1] = true;
            }
        }

        public bool IsCurrentHandled(Event e)
        {
            return m_count > 0
                   && ReferenceEquals(m_events[m_count - 1], e)
                   && m_handled[m_count - 1];
        }

        public void PopIfCurrent(Event e)
        {
            if (!IsCurrent(e))
            {
                return;
            }

            m_count--;
            m_events[m_count] = null;
            m_handled[m_count] = false;
        }

        private void EnsureCapacity(int min)
        {
            if (m_events.Length >= min)
            {
                return;
            }

            int newSize = Math.Max(min, m_events.Length * 2);
            Array.Resize(ref m_events, newSize);
            Array.Resize(ref m_handled, newSize);
        }
    }
}
