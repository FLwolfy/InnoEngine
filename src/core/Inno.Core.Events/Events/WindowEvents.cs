namespace Inno.Core.Events;

/// <summary>
/// Base class for window lifecycle events.
/// </summary>
public abstract class WindowEvent(uint windowId) : Event
{
    /// <summary>
    /// Gets the source window id for this event.
    /// </summary>
    public uint windowId { get; } = windowId;
}


/// <summary>
/// Raised when the window size changes.
/// </summary>
public class WindowResizeEvent(uint windowId, int width, int height) : WindowEvent(windowId)
{
    /// <summary>
    /// Gets the new window width.
    /// </summary>
    public int width { get; } = width;

    /// <summary>
    /// Gets the new window height.
    /// </summary>
    public int height { get; } = height;
}

/// <summary>
/// Raised when the window requests close.
/// </summary>
public class WindowCloseEvent(uint windowId) : WindowEvent(windowId)
{
}

/// <summary>
/// Raised when a window gains or loses input focus.
/// </summary>
public class WindowFocusChangedEvent(uint windowId, bool isFocused) : WindowEvent(windowId)
{
    /// <summary>
    /// Gets whether the window has input focus after the change.
    /// </summary>
    public bool isFocused { get; } = isFocused;
}
