using System;

namespace Inno.Platform;

/// <summary>
/// Describes one opaque event emitted by the active native window backend.
/// </summary>
/// <param name="backendName">The stable backend name, such as <c>SDL3</c>.</param>
/// <param name="data">A backend event pointer valid only during the extension callback.</param>
public readonly record struct PlatformNativeEvent(
    string backendName,
    IntPtr data);
