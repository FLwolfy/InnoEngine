namespace Inno.Core.Events;

/// <summary>
/// Base class for window lifecycle events.
/// </summary>
public abstract class WindowEvent : Event
{
}


/// <summary>
/// Raised when the window size changes.
/// </summary>
public class WindowResizeEvent(int width, int height) : WindowEvent
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
public class WindowCloseEvent : WindowEvent
{
}
