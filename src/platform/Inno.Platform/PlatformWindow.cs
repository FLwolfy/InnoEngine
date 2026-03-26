using System;
using Inno.Native.SDL3;

namespace Inno.Platform;

internal sealed class PlatformWindow : IPlatformWindow
{
    private SDLWindowPtr m_window;
    private bool m_disposed;

    internal unsafe PlatformWindow(SDLWindowPtr window, string title)
    {
        m_window = window;
        this.title = title;
        windowId = SDL.GetWindowID(m_window);
        var currentWidth = 0;
        var currentHeight = 0;
        SDL.GetWindowSize(m_window, ref currentWidth, ref currentHeight);
        width = currentWidth;
        height = currentHeight;
        nativeHandles = GetNativeHandles(m_window);
    }

    public uint windowId { get; }

    public string title { get; }

    public int width { get; private set; }

    public int height { get; private set; }

    public bool isClosed { get; private set; }

    public PlatformNativeHandles nativeHandles { get; }

    internal void UpdateSize(int width, int height)
    {
        this.width = width;
        this.height = height;
    }

    internal void MarkClosed()
    {
        isClosed = true;
    }

    public void RequestClose()
    {
        isClosed = true;
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        if (!m_window.IsNull)
        {
            SDL.DestroyWindow(m_window);
            m_window = SDLWindowPtr.Null;
        }

        isClosed = true;
        m_disposed = true;
    }

    private static unsafe PlatformNativeHandles GetNativeHandles(SDLWindowPtr window)
    {
        var props = SDL.GetWindowProperties(window);
        var kind = PlatformNativeHandleKind.Unknown;
        IntPtr windowHandle = IntPtr.Zero;
        IntPtr displayHandle = IntPtr.Zero;

        if (OperatingSystem.IsWindows())
        {
            kind = PlatformNativeHandleKind.Win32;
            windowHandle = (IntPtr)SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_WIN32_HWND_POINTER, (void*)0);
        }
        else if (OperatingSystem.IsMacOS())
        {
            kind = PlatformNativeHandleKind.Cocoa;
            windowHandle = (IntPtr)SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_COCOA_WINDOW_POINTER, (void*)0);
        }
        else if (OperatingSystem.IsLinux())
        {
            var waylandSurface = (IntPtr)SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_WAYLAND_SURFACE_POINTER, (void*)0);
            if (waylandSurface != IntPtr.Zero)
            {
                kind = PlatformNativeHandleKind.Wayland;
                windowHandle = waylandSurface;
                displayHandle = (IntPtr)SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_WAYLAND_DISPLAY_POINTER, (void*)0);
            }
            else
            {
                kind = PlatformNativeHandleKind.X11;
                displayHandle = (IntPtr)SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_X11_DISPLAY_POINTER, (void*)0);
                windowHandle = (IntPtr)SDL.GetNumberProperty(props, SDL.SDL_PROP_WINDOW_X11_WINDOW_NUMBER, 0);
            }
        }

        return new PlatformNativeHandles(windowHandle, displayHandle, kind);
    }
}
