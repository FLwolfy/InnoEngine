using System;
using System.Collections.Generic;
using System.Threading;

namespace Inno.Core.Events;

/// <summary>
/// Base type for all engine events.
/// </summary>
public abstract class Event
{
    private int m_globalHandled;
    [ThreadStatic]
    private static Stack<HubDispatchFrame>? t_hubFrames;

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
        Stack<HubDispatchFrame>? frames = t_hubFrames;
        if (frames is null || frames.Count == 0 || !ReferenceEquals(frames.Peek().eventRef, this))
        {
            throw new InvalidOperationException("HandleInHub can only be used while this event is being dispatched in a hub.");
        }

        frames.Peek().handledInHub = true;
    }

    internal bool isGlobalHandled => Volatile.Read(ref m_globalHandled) == 1;

    internal HubDispatchScope BeginHubDispatchScope()
    {
        Stack<HubDispatchFrame>? frames = t_hubFrames;
        if (frames is null)
        {
            frames = new Stack<HubDispatchFrame>(4);
            t_hubFrames = frames;
        }

        HubDispatchFrame frame = new(this);
        frames.Push(frame);
        return new HubDispatchScope(frames, frame);
    }

    internal bool IsHandledInCurrentHub()
    {
        Stack<HubDispatchFrame>? frames = t_hubFrames;
        return frames is not null
               && frames.Count > 0
               && ReferenceEquals(frames.Peek().eventRef, this)
               && frames.Peek().handledInHub;
    }

    internal readonly struct HubDispatchScope(Stack<HubDispatchFrame> frames, HubDispatchFrame frame) : IDisposable
    {
        public void Dispose()
        {
            if (frames.Count == 0)
            {
                return;
            }

            if (ReferenceEquals(frames.Peek(), frame))
            {
                frames.Pop();
            }
        }
    }

    internal sealed class HubDispatchFrame(Event eventRef)
    {
        public Event eventRef { get; } = eventRef;
        public bool handledInHub { get; set; }
    }
}
