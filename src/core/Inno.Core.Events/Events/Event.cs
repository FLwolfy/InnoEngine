namespace Inno.Core.Events;

public abstract class Event
{
    public abstract EventType type { get; }
    public abstract EventCategory category { get; }
    
    public bool handled { get; set; } = false;

    public bool IsInCategory(EventCategory c)
    {
        return (this.category & c) != 0;
    }
}

