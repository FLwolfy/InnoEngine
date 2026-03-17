namespace Inno.Core.Events;

public abstract class WindowEvent : Event
{
    public sealed override EventCategory category => EventCategory.Application;
}


public class WindowResizeEvent(int width, int height) : WindowEvent
{
    public int width { get; } = width;
    public int height { get; } = height;

    public override EventType type => EventType.WindowResize;
}

public class WindowCloseEvent : WindowEvent
{
    public override EventType type => EventType.WindowClose;
}