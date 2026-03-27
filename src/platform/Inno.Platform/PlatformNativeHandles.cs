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
);
