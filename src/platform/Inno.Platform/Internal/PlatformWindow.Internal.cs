using System;
using Inno.Native.SDL3;

namespace Inno.Platform;

/// <summary>
/// Represents a native platform window.
/// </summary>
public sealed partial class PlatformWindow
{
    private SDLWindowPtr m_window;
    private readonly uint m_windowId;
    private readonly string m_title;
    private readonly bool m_ownsNativeWindow;
    private int m_width;
    private int m_height;
    private bool m_isClosed;
    private readonly PlatformNativeHandles m_nativeHandles;
    private bool m_disposed;
    internal SDLWindowPtr sdlWindow => m_window;

    internal unsafe PlatformWindow(SDLWindowPtr window, string title)
        : this(window, title, ownsNativeWindow: true)
    {
    }

    internal unsafe PlatformWindow(SDLWindowPtr window, string title, bool ownsNativeWindow)
    {
        m_window = window;
        m_title = title;
        m_ownsNativeWindow = ownsNativeWindow;
        m_windowId = SDL.GetWindowID(m_window);

        var currentWidth = 0;
        var currentHeight = 0;
        SDL.GetWindowSize(m_window, ref currentWidth, ref currentHeight);
        m_width = currentWidth;
        m_height = currentHeight;
        m_nativeHandles = GetNativeHandles(m_window) with
        {
            backendName = "SDL3",
            backendWindowHandle = (IntPtr)m_window.Handle
        };
    }

    internal void UpdateSize(int width, int height)
    {
        m_width = width;
        m_height = height;
    }

    internal void MarkClosed()
    {
        m_isClosed = true;
    }
    
    public partial void RequestClose()
    {
        m_isClosed = true;
    }

    public partial void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        if (!m_window.IsNull && m_ownsNativeWindow)
        {
            SDL.DestroyWindow(m_window);
        }

        m_window = SDLWindowPtr.Null;
        m_isClosed = true;
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

        return new PlatformNativeHandles(windowHandle, displayHandle, kind);
    }
}
