using System;

namespace Inno.Platform;

/// <summary>
/// Represents a backend-neutral native window owned by a platform application.
/// </summary>
public interface IPlatformWindow : IDisposable
{
    /// <summary>
    /// Gets the application-local identifier of this window.
    /// </summary>
    uint windowId { get; }

    /// <summary>
    /// Gets the title captured when the window was created.
    /// </summary>
    string title { get; }

    /// <summary>
    /// Gets the logical client width.
    /// </summary>
    int width { get; }

    /// <summary>
    /// Gets the logical client height.
    /// </summary>
    int height { get; }

    /// <summary>
    /// Gets the drawable width in physical pixels.
    /// </summary>
    int pixelWidth { get; }

    /// <summary>
    /// Gets the drawable height in physical pixels.
    /// </summary>
    int pixelHeight { get; }

    /// <summary>
    /// Gets whether the window has received or requested a close operation.
    /// </summary>
    bool isClosed { get; }

    /// <summary>
    /// Gets whether the window currently owns platform input focus.
    /// </summary>
    bool isFocused { get; }

    /// <summary>
    /// Gets the operating-system surface handles required by a graphics backend.
    /// </summary>
    PlatformNativeHandles nativeHandles { get; }

    /// <summary>
    /// Marks the window as requesting closure without destroying it immediately.
    /// </summary>
    void RequestClose();
}
