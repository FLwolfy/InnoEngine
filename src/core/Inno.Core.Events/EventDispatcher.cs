using System;
using System.Collections.Generic;

namespace Inno.Core.Events;

public class EventDispatcher
{
    private readonly Queue<Event> m_eventQueue = new();

    public void PushEvent(Event e) => m_eventQueue.Enqueue(e);

    public void Dispatch(Action<Event> onEvent)
    {
        while (m_eventQueue.Count > 0)
        {
            Event e = m_eventQueue.Dequeue();
            onEvent.Invoke(e);
        }
    }
}