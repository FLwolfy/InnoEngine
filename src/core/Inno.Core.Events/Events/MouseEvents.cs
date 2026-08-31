using Inno.Core.Input;

namespace Inno.Core.Events;

/// <summary>
/// Base class for mouse events.
/// </summary>
public abstract class MouseEvent(uint windowId) : Event
{
    /// <summary>
    /// Gets the source window id for this event.
    /// </summary>
    public uint windowId { get; } = windowId;
}


/// <summary>
/// Raised when the cursor moves.
/// </summary>
public class MouseMovedEvent(uint windowId, float x, float y) : MouseEvent(windowId)
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
public class MouseScrolledEvent(uint windowId, float offsetX, float offsetY) : MouseEvent(windowId)
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
public abstract class MouseButtonEvent(uint windowId, MouseButton button) : MouseEvent(windowId)
{
    /// <summary>
    /// Gets the mouse button for this event.
    /// </summary>
    public MouseButton button { get; } = button;
}

/// <summary>
/// Raised when a mouse button is pressed.
/// </summary>
public class MouseButtonPressedEvent(uint windowId, MouseButton button) : MouseButtonEvent(windowId, button)
{
}

/// <summary>
/// Raised when a mouse button is released.
/// </summary>
public class MouseButtonReleasedEvent(uint windowId, MouseButton button) : MouseButtonEvent(windowId, button)
{
}
