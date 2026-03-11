using System;

using Inno.Core.Events;
using Inno.Core.Math;

using Inno.Platform.Graphics;

using Veldrid;
using Veldrid.Sdl2;

namespace Inno.Platform.Window.Bridge;

internal class VeldridSdl2Window : IWindow
{
    internal Sdl2Window inner { get; }
    
    // Event and Inputs
    private InputSnapshot m_inputSnapshot;
    
    private readonly EventSnapshot m_eventSnapshot = new();
    
    private bool m_isWindowSizeDirty;

    // Properties
    public bool exists => inner.Exists;
    public IFrameBuffer frameBuffer { get; internal set; } = null!;
    public Vector2Int position
    {
        get => new(inner.X, inner.Y);
        set
        {
            inner.X = value.x;
            inner.Y = value.y;
        }
    }
    public Vector2Int size
    {
        get => new(inner.Width, inner.Height);
        set
        {
            inner.Width = value.x;
            inner.Height = value.y;
        }
    }

    public Rect bounds
    {
        get
        { 
            var innerBounds = inner.Bounds;
            return new Rect(innerBounds.X, innerBounds.Y, innerBounds.Width, innerBounds.Height);
        }
    }

    public bool focused
    {
        get
        {
            SDL_WindowFlags flags = Sdl2Native.SDL_GetWindowFlags(inner.SdlWindowHandle);
            return (flags & SDL_WindowFlags.InputFocus) != 0;
        }
    }

    public bool minimized
    {
        get
        {
            SDL_WindowFlags flags = Sdl2Native.SDL_GetWindowFlags(inner.SdlWindowHandle);
            return (flags & SDL_WindowFlags.Minimized) != 0;
        }
    }

    public bool resizable
    {
        get => inner.Resizable;
        set => inner.Resizable = value;
    }
    public bool decorated
    {
        get => inner.BorderVisible;
        set => inner.BorderVisible = value;
    }
    public string title
    {
        get => inner.Title;
        set => inner.Title = value;
    }

    // Constructor
    public VeldridSdl2Window(WindowInfo info)
    {
        var flags = MapToSdlFlags(info.flags);
        flags |= SDL_WindowFlags.AllowHighDpi; // TODO: Move this outside
        flags |= SDL_WindowFlags.Resizable;

        inner = new Sdl2Window(
            info.name,
            100, 100,
            info.width,
            info.height,
            flags,
            false);

        Resized += () => m_isWindowSizeDirty = true;
        inner.Resized += () => Resized?.Invoke();
        inner.Moved += p => Moved?.Invoke(new Vector2(p.X, p.Y));
        inner.Closed += () => Closed?.Invoke();
        inner.FocusGained += () => FocusGained?.Invoke();
        
        m_inputSnapshot = inner.PumpEvents();
    }

    // States
    public void Show() => Sdl2Native.SDL_ShowWindow(inner.SdlWindowHandle);
    public void Hide() => Sdl2Native.SDL_HideWindow(inner.SdlWindowHandle);
    public void Focus() => Sdl2Native.SDL_RaiseWindow(inner.SdlWindowHandle);
    public void Close() => inner.Close();
    
    // Actions
    public event Action? Resized;
    public event Action? Closed;
    public event Action<Vector2>? Moved;
    public event Action? FocusGained;
    
    // Methods

    public void PumpEvents(EventDispatcher? dispatcher)
    {
        // Input Events
        m_inputSnapshot = inner.PumpEvents();
        m_eventSnapshot.Clear();
        VeldridSdl2InputAdapter.AdaptInputEvents(m_inputSnapshot, e =>
        {
            m_eventSnapshot.AddEvent(e);
            dispatcher?.PushEvent(e);
        });
        foreach (var c in m_inputSnapshot.KeyCharPresses)
        {
            m_eventSnapshot.AddInputChar(c);
        }
        
        // Application Events
        if (m_isWindowSizeDirty)
        {
            var resizeEvent = new WindowResizeEvent(size.x, size.y);
            m_eventSnapshot.AddEvent(resizeEvent);
            dispatcher?.PushEvent(resizeEvent);
            
            m_isWindowSizeDirty = false;
        }
        if (!exists)
        {
            m_eventSnapshot.AddEvent(new WindowCloseEvent());
            dispatcher?.PushEvent(new WindowCloseEvent());
        }
    }
    
    public EventSnapshot GetPumpedEvents() => m_eventSnapshot;

    public Vector2Int GetFrameBufferSize() => VeldridSdl2HiDpi.GetFramebufferSize(inner);
    public Vector2 GetFrameBufferScale() => VeldridSdl2HiDpi.GetFramebufferScale(inner);

    public void Dispose()
    {
        inner.Close();
    }
      
    private static SDL_WindowFlags MapToSdlFlags(WindowFlags flags)
    {
        SDL_WindowFlags sdl = 0;

        // Visibility
        if (flags.HasFlag(WindowFlags.Hidden))
            sdl |= SDL_WindowFlags.Hidden;
        else
            sdl |= SDL_WindowFlags.Shown;

        // Decorated / Resize
        if (flags.HasFlag(WindowFlags.Resizable))
            sdl |= SDL_WindowFlags.Resizable;

        if (!flags.HasFlag(WindowFlags.Decorated))
            sdl |= SDL_WindowFlags.Borderless;

        // DPI
        if (flags.HasFlag(WindowFlags.AllowHighDpi))
            sdl |= SDL_WindowFlags.AllowHighDpi;

        // Z-order / taskbar
        if (flags.HasFlag(WindowFlags.AlwaysOnTop))
            sdl |= SDL_WindowFlags.AlwaysOnTop;

        if (flags.HasFlag(WindowFlags.SkipTaskbar))
            sdl |= SDL_WindowFlags.SkipTaskbar;

        // Window type semantics
        if (flags.HasFlag(WindowFlags.ToolWindow))
            sdl |= SDL_WindowFlags.Utility;

        if (flags.HasFlag(WindowFlags.Popup))
            sdl |= SDL_WindowFlags.PopupMenu;

        // Fullscreen
        if (flags.HasFlag(WindowFlags.Fullscreen))
            sdl |= SDL_WindowFlags.FullScreenDesktop;

        return sdl;
    }
}