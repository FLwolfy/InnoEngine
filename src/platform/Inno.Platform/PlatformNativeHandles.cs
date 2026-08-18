using System;

namespace Inno.Platform;

/// <summary>
/// Native handles associated with a platform window.
/// </summary>
/// <param name="windowHandle">Native window handle.</param>
/// <param name="displayHandle">Native display handle when required by the platform.</param>
/// <param name="handleKind">Platform handle kind.</param>
public readonly record struct PlatformNativeHandles(
    IntPtr windowHandle,
    IntPtr displayHandle = default,
    PlatformNativeHandleKind handleKind = PlatformNativeHandleKind.Unknown
)
{
    /// <summary>
    /// Gets the active window backend name, such as <c>SDL3</c>.
    /// </summary>
    public string backendName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the opaque window handle owned by the active window backend.
    /// </summary>
    public IntPtr backendWindowHandle { get; init; }
}
