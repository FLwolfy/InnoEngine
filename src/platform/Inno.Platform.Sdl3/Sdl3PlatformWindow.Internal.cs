using System;
using Inno.Native.Sdl3;
using Inno.Platform;

namespace Inno.Platform.Sdl3;

/// <summary>
/// Represents a native platform window.
/// </summary>
public sealed partial class Sdl3PlatformWindow
{
    private SDLWindowPtr m_window;
    private readonly uint m_windowId;
    private readonly string m_title;
    private readonly bool m_ownsNativeWindow;
    private int m_width;
    private int m_height;
    private int m_pixelWidth;
    private int m_pixelHeight;
    private bool m_isClosed;
    private bool m_isFocused;
    private readonly PlatformNativeHandles m_nativeHandles;
    private readonly nint m_sdlWindowHandle;
    private bool m_disposed;
    internal SDLWindowPtr sdlWindow => m_window;

    internal unsafe Sdl3PlatformWindow(SDLWindowPtr window, string title)
        : this(window, title, ownsNativeWindow: true)
    {
    }

    internal unsafe Sdl3PlatformWindow(SDLWindowPtr window, string title, bool ownsNativeWindow)
    {
        m_window = window;
        m_sdlWindowHandle = (nint)window.Handle;
        m_title = title;
        m_ownsNativeWindow = ownsNativeWindow;
        m_windowId = SDL.GetWindowID(m_window);

        var currentWidth = 0;
        var currentHeight = 0;
        SDL.GetWindowSize(m_window, ref currentWidth, ref currentHeight);
        m_width = currentWidth;
        m_height = currentHeight;
        RefreshPixelSize();
        m_isFocused = ((SDLWindowFlags)SDL.GetWindowFlags(m_window) & SDLWindowFlags.InputFocus) != 0;
        m_nativeHandles = GetNativeHandles(m_window);
    }

    internal void UpdateLogicalSize(int width, int height)
    {
        m_width = width;
        m_height = height;
        RefreshPixelSize();
    }

    internal void UpdatePixelSize(int width, int height)
    {
        m_pixelWidth = Math.Max(1, width);
        m_pixelHeight = Math.Max(1, height);
    }

    internal void MarkClosed()
    {
        m_isClosed = true;
    }

    internal void UpdateFocus(bool isFocused)
    {
        m_isFocused = isFocused;
    }

    private void RefreshPixelSize()
    {
        var pixelWidth = 0;
        var pixelHeight = 0;
        SDL.GetWindowSizeInPixels(m_window, ref pixelWidth, ref pixelHeight);
        m_pixelWidth = pixelWidth > 0 ? pixelWidth : Math.Max(1, m_width);
        m_pixelHeight = pixelHeight > 0 ? pixelHeight : Math.Max(1, m_height);
    }
    
    /// <summary>
    /// Queues a close request for processing at the next platform safety point.
    /// </summary>
    public partial void RequestClose()
    {
        m_isClosed = true;
    }

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
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
        m_isFocused = false;
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
