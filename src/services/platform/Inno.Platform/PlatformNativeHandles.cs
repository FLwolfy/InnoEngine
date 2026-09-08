using System;

namespace Inno.Platform;

/// <summary>
/// Carries operating-system surface handles without exposing the active windowing backend.
/// </summary>
/// <param name="windowHandle">
/// Native window handle.
/// </param>
/// <param name="displayHandle">
/// Native display handle when required by the platform.
/// </param>
/// <param name="handleKind">
/// Platform handle kind.
/// </param>
/// <returns>
/// A backend-neutral value that may be passed to a graphics adapter.
/// </returns>
public readonly record struct PlatformNativeHandles(
    IntPtr windowHandle,
    IntPtr displayHandle = default,
    PlatformNativeHandleKind handleKind = PlatformNativeHandleKind.Unknown
);
