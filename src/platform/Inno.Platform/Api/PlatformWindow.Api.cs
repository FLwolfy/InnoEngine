using System;

namespace Inno.Platform;

/// <summary>
/// Represents a native platform window.
/// </summary>
public sealed partial class PlatformWindow : IDisposable
{
    /// <summary>
    /// Gets the platform window identifier.
    /// </summary>
    public uint windowId => m_windowId;

    /// <summary>
    /// Gets the window title.
    /// </summary>
    public string title => m_title;

    /// <summary>
    /// Gets the current window width in pixels.
    /// </summary>
    public int width => m_width;

    /// <summary>
    /// Gets the current window height in pixels.
    /// </summary>
    public int height => m_height;

    /// <summary>
    /// Gets whether this window has been marked as closed.
    /// </summary>
    public bool isClosed => m_isClosed;

    /// <summary>
    /// Gets native window handles for graphics backends and platform integration.
    /// </summary>
    public PlatformNativeHandles nativeHandles => m_nativeHandles;

    /// <summary>
    /// Marks this window as requesting close.
    /// </summary>
    public partial void RequestClose();

    /// <summary>
    /// Releases this instance and its resources, destroying the underlying native window when owned by this instance.
    /// </summary>
    public partial void Dispose();
}
