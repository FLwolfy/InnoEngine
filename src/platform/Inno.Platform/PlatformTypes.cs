using System;

namespace Inno.Platform;

public enum PlatformNativeHandleKind
{
    Unknown = 0,
    Win32,
    Cocoa,
    Wayland,
    X11
}

public readonly record struct PlatformNativeHandles(
    IntPtr windowHandle,
    IntPtr displayHandle = default,
    PlatformNativeHandleKind handleKind = PlatformNativeHandleKind.Unknown);

public sealed class PlatformWindowOptions
{
    public string title { get; init; } = "Inno Window";

    public int width { get; init; } = 1280;

    public int height { get; init; } = 720;

    public bool resizable { get; init; } = true;

    public bool highPixelDensity { get; init; } = true;
}

public enum PlatformEventType
{
    None = 0,
    QuitRequested,
    WindowResized,
    WindowCloseRequested
}

public readonly record struct PlatformEvent(
    PlatformEventType type,
    uint windowId = 0,
    int width = 0,
    int height = 0);
