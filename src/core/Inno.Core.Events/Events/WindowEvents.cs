namespace Inno.Core.Events;

/// <summary>
/// Base class for window lifecycle events.
/// </summary>
/// <param name="windowId">
/// The window id used to initialize this instance.
/// </param>
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
/// <param name="windowId">
/// The window id used to initialize this instance.
/// </param>
/// <param name="width">
/// The width in logical units or pixels required by this operation.
/// </param>
/// <param name="height">
/// The height in logical units or pixels required by this operation.
/// </param>
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
/// <param name="windowId">
/// The window id used to initialize this instance.
/// </param>
public class WindowCloseEvent(uint windowId) : WindowEvent(windowId)
{
}

/// <summary>
/// Raised when a window gains or loses input focus.
/// </summary>
/// <param name="windowId">
/// The window id used to initialize this instance.
/// </param>
/// <param name="isFocused">
/// The is focused used to initialize this instance.
/// </param>
public class WindowFocusChangedEvent(uint windowId, bool isFocused) : WindowEvent(windowId)
{
    /// <summary>
    /// Gets whether the window has input focus after the change.
    /// </summary>
    public bool isFocused { get; } = isFocused;
}
