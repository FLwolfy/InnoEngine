using Inno.Core.Input;

namespace Inno.Core.Events;

/// <summary>
/// Base class for mouse events.
/// </summary>
/// <param name="windowId">
/// The window id used to initialize this instance.
/// </param>
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
/// <param name="windowId">
/// The window id used to initialize this instance.
/// </param>
/// <param name="x">
/// The horizontal or first component.
/// </param>
/// <param name="y">
/// The vertical or second component.
/// </param>
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
/// <param name="windowId">
/// The window id used to initialize this instance.
/// </param>
/// <param name="offsetX">
/// The offset x used to initialize this instance.
/// </param>
/// <param name="offsetY">
/// The offset y used to initialize this instance.
/// </param>
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
/// <param name="windowId">
/// The window id used to initialize this instance.
/// </param>
/// <param name="button">
/// The button used to initialize this instance.
/// </param>
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
/// <param name="windowId">
/// The window id used to initialize this instance.
/// </param>
/// <param name="button">
/// The button used to initialize this instance.
/// </param>
public class MouseButtonPressedEvent(uint windowId, MouseButton button) : MouseButtonEvent(windowId, button)
{
}

/// <summary>
/// Raised when a mouse button is released.
/// </summary>
/// <param name="windowId">
/// The window id used to initialize this instance.
/// </param>
/// <param name="button">
/// The button used to initialize this instance.
/// </param>
public class MouseButtonReleasedEvent(uint windowId, MouseButton button) : MouseButtonEvent(windowId, button)
{
}
