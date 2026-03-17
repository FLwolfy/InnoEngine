using Inno.Core.Input;

namespace Inno.Core.Events;

public abstract class MouseEvent : Event
{
    public sealed override EventCategory category => EventCategory.Input | EventCategory.Mouse;
}


public class MouseMovedEvent(float x, float y) : MouseEvent
{
    public float x { get; } = x;
    public float y { get; } = y;
    public override EventType type => EventType.MouseMoved;
}

public class MouseScrolledEvent(float offsetX, float offsetY) : MouseEvent
{
    public float offsetX { get; } = offsetX;
    public float offsetY { get; } = offsetY;
    public override EventType type => EventType.MouseScrolled;
}

public abstract class MouseButtonEvent(MouseButton button) : MouseEvent
{
    public MouseButton button { get; } = button;
}

public class MouseButtonPressedEvent(MouseButton button) : MouseButtonEvent(button)
{
    public override EventType type => EventType.MouseButtonPressed;
}

public class MouseButtonReleasedEvent(MouseButton button) : MouseButtonEvent(button)
{
    public override EventType type => EventType.MouseButtonReleased;
}