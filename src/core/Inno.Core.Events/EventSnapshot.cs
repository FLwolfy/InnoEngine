using System.Collections.Generic;

namespace Inno.Core.Events;

public class EventSnapshot
{
    private readonly Dictionary<EventType, object> m_eventMap = new();
    private readonly List<char> m_inputChars = new();
    
    public void AddEvent(Event e)
    {
        if (m_eventMap.TryGetValue(e.type, out var existing))
        {
            if (existing is List<Event> list)
                list.Add(e);
        }
        else
        {
            m_eventMap[e.type] = new List<Event> { e };
        }
    }
    
    public IEnumerable<Event> GetEvents(EventType type)
    {
        if (m_eventMap.TryGetValue(type, out var obj) && obj is List<Event> list)
            return list;
        return [];
    }

    public void AddInputChar(char c)
    {
        m_inputChars.Add(c);
    }
    
    public IReadOnlyList<char> GetInputChars() => m_inputChars;

    public void Clear()
    {
        m_eventMap.Clear();
        m_inputChars.Clear();
    }
}
