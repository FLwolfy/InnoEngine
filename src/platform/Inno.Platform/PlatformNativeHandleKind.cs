namespace Inno.Platform;

/// <summary>
/// Identifies the native platform window handle type.
/// </summary>
public enum PlatformNativeHandleKind
{
    /// <summary>
    /// Unknown or unsupported native handle type.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Win32 window handle.
    /// </summary>
    Win32,

    /// <summary>
    /// Cocoa window handle.
    /// </summary>
    Cocoa,
}
