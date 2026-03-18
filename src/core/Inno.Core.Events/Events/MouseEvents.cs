using Inno.Core.Input;

namespace Inno.Core.Events;

/// <summary>
/// Base class for mouse events.
/// </summary>
public abstract class MouseEvent : Event
{
}


/// <summary>
/// Raised when the cursor moves.
/// </summary>
public class MouseMovedEvent(float x, float y) : MouseEvent
{
    /// <summary>
    /// Gets cursor X coordinate.
    /// </summary>
    public float x { get; } = x;

    /// <summary>
    /// Gets cursor Y coordinate.
    /// </summary>
    public float y { get; } = y;
}

/// <summary>
/// Raised when mouse wheel scrolls.
/// </summary>
public class MouseScrolledEvent(float offsetX, float offsetY) : MouseEvent
{
    /// <summary>
    /// Gets horizontal scroll offset.
    /// </summary>
    public float offsetX { get; } = offsetX;

    /// <summary>
    /// Gets vertical scroll offset.
    /// </summary>
    public float offsetY { get; } = offsetY;
}

/// <summary>
/// Base class for mouse button events.
/// </summary>
public abstract class MouseButtonEvent(MouseButton button) : MouseEvent
{
    /// <summary>
    /// Gets the mouse button for this event.
    /// </summary>
    public MouseButton button { get; } = button;
}

/// <summary>
/// Raised when a mouse button is pressed.
/// </summary>
public class MouseButtonPressedEvent(MouseButton button) : MouseButtonEvent(button)
{
}

/// <summary>
/// Raised when a mouse button is released.
/// </summary>
public class MouseButtonReleasedEvent(MouseButton button) : MouseButtonEvent(button)
{
}
