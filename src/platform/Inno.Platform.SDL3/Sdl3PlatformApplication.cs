using System;
using System.Collections.Generic;
using Inno.Native.SDL3;
using Inno.Platform;

namespace Inno.Platform.SDL3;

public sealed class Sdl3PlatformApplication : IPlatformApplication
{
    private readonly Dictionary<uint, Sdl3PlatformWindow> m_windows = [];
    private bool m_disposed;

    public Sdl3PlatformApplication()
    {
        if (!SDL.Init((uint)(SDLInitFlags.Video | SDLInitFlags.Events)))
        {
            throw SDL.GetErrorAsException() ?? new InvalidOperationException("SDL_Init failed.");
        }
    }

    public IPlatformWindow CreateWindow(PlatformWindowOptions options)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(options);

        var flags = options.highPixelDensity ? SDLWindowFlags.HighPixelDensity : 0;

        if (options.resizable)
        {
            flags |= SDLWindowFlags.Resizable;
        }

        var windowHandle = SDL.CreateWindow(options.title, options.width, options.height, (ulong)flags);
        if (windowHandle.IsNull)
        {
            throw SDL.GetErrorAsException() ?? new InvalidOperationException("SDL_CreateWindow failed.");
        }

        var window = new Sdl3PlatformWindow(windowHandle, options.title);
        m_windows[window.windowId] = window;
        return window;
    }

    public bool PollEvent(out PlatformEvent platformEvent)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        SDLEvent evnt = default;
        if (!SDL.PollEvent(ref evnt))
        {
            platformEvent = default;
            return false;
        }

        var eventType = (SDLEventType)evnt.Type;
        switch (eventType)
        {
            case SDLEventType.Quit:
                platformEvent = new PlatformEvent(PlatformEventType.QuitRequested);
                return true;
            case SDLEventType.WindowResized:
            case SDLEventType.WindowPixelSizeChanged:
            {
                if (m_windows.TryGetValue(evnt.Window.WindowID, out var resizedWindow))
                {
                    resizedWindow.UpdateSize(evnt.Window.Data1, evnt.Window.Data2);
                }

                platformEvent = new PlatformEvent(
                    PlatformEventType.WindowResized,
                    evnt.Window.WindowID,
                    evnt.Window.Data1,
                    evnt.Window.Data2);
                return true;
            }
            case SDLEventType.WindowCloseRequested:
            {
                if (m_windows.TryGetValue(evnt.Window.WindowID, out var closingWindow))
                {
                    closingWindow.MarkClosed();
                }

                platformEvent = new PlatformEvent(PlatformEventType.WindowCloseRequested, evnt.Window.WindowID);
                return true;
            }
            default:
                platformEvent = new PlatformEvent(PlatformEventType.None);
                return true;
        }
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        foreach (var window in m_windows.Values)
        {
            window.Dispose();
        }

        m_windows.Clear();
        SDL.Quit();
        m_disposed = true;
    }
}
