using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Inno.Core.Events;
using Inno.Core.Input;
using Inno.Native.SDL3;

namespace Inno.Platform;

/// <summary>
/// Platform runtime entry point responsible for window creation and platform event polling.
/// </summary>
public sealed unsafe partial class PlatformApplication
{
    private readonly Dictionary<uint, PlatformWindow> m_windows = [];
    private SDLEventFilter? m_liveResizeEventWatch;
    private SDLMainThreadCallback? m_liveResizeMainThreadCallback;
    private GCHandle m_liveResizeEventWatchHandle;
    private int m_liveResizeRedrawQueued;
    private uint m_liveResizeWindowId;
    private bool m_disposed;

    private void Initialize()
    {
        if (OperatingSystem.IsMacOS())
        {
            // Match traditional key repeat behavior (disable accent press-and-hold popup).
            _ = SDL.SetHint(SDL.SDL_HINT_MAC_PRESS_AND_HOLD, "0");
        }

        if (!SDL.Init((uint)(SDLInitFlags.Video | SDLInitFlags.Events)))
        {
            throw SDL.GetErrorAsException() ?? new InvalidOperationException("SDL_Init failed.");
        }

        m_liveResizeEventWatch = LiveResizeEventWatch;
        m_liveResizeMainThreadCallback = LiveResizeMainThreadCallback;
        m_liveResizeEventWatchHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        var userData = (nint)GCHandle.ToIntPtr(m_liveResizeEventWatchHandle);
        if (!SDL.AddEventWatch(m_liveResizeEventWatch, userData))
        {
            m_liveResizeEventWatchHandle.Free();
            m_liveResizeEventWatch = null;
            m_liveResizeMainThreadCallback = null;
        }
    }

    public partial PlatformWindow CreateWindow(PlatformWindowOptions options)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

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

        var window = new PlatformWindow(windowHandle, options.title);
        m_windows[window.windowId] = window;
        return window;
    }

    public partial bool PollEvent(out Event? evnt)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        SDLEvent sdlEvent = default;
        while (SDL.PollEvent(ref sdlEvent))
        {
            PlatformApplicationHooks.DispatchSdlEvent(this, ref sdlEvent);

            if (TryTranslateEvent(ref sdlEvent, out evnt))
            {
                return true;
            }
        }

        evnt = null;
        return false;
    }

    public partial void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        if (m_liveResizeEventWatch is not null && m_liveResizeEventWatchHandle.IsAllocated)
        {
            var userData = (nint)GCHandle.ToIntPtr(m_liveResizeEventWatchHandle);
            SDL.RemoveEventWatch(m_liveResizeEventWatch, userData);
            m_liveResizeEventWatchHandle.Free();
            m_liveResizeEventWatch = null;
            m_liveResizeMainThreadCallback = null;
        }

        PlatformApplicationHooks.OnDisposing(this);

        foreach (var window in m_windows.Values)
        {
            window.Dispose();
        }

        m_windows.Clear();
        SDL.Quit();
        m_disposed = true;
    }

    private static unsafe byte LiveResizeEventWatch(void* userData, SDLEvent* evnt)
    {
        if (userData == null || evnt == null)
        {
            return 1;
        }

        var eventType = (SDLEventType)evnt->Type;
        if (eventType != SDLEventType.WindowExposed || evnt->Window.Data1 == 0)
        {
            return 1;
        }

        var handle = GCHandle.FromIntPtr((IntPtr)userData);
        if (handle.Target is not PlatformApplication application || application.m_disposed)
        {
            return 1;
        }

        Volatile.Write(ref application.m_liveResizeWindowId, evnt->Window.WindowID);
        if (Interlocked.Exchange(ref application.m_liveResizeRedrawQueued, 1) == 0)
        {
            var userDataPtr = (nint)GCHandle.ToIntPtr(application.m_liveResizeEventWatchHandle);
            if (!SDL.RunOnMainThread(application.m_liveResizeMainThreadCallback!, userDataPtr, false))
            {
                Interlocked.Exchange(ref application.m_liveResizeRedrawQueued, 0);
            }
        }

        return 1;
    }

    private static unsafe void LiveResizeMainThreadCallback(void* userData)
    {
        if (userData == null)
        {
            return;
        }

        var handle = GCHandle.FromIntPtr((IntPtr)userData);
        if (handle.Target is not PlatformApplication application || application.m_disposed)
        {
            return;
        }

        var windowId = Volatile.Read(ref application.m_liveResizeWindowId);
        Interlocked.Exchange(ref application.m_liveResizeRedrawQueued, 0);
        if (windowId != 0)
        {
            PlatformApplicationHooks.DispatchLiveResizeRedraw(application, windowId);
        }
    }

    private bool TryTranslateEvent(ref SDLEvent sdlEvent, out Event? evnt)
    {
        var eventType = (SDLEventType)sdlEvent.Type;
        switch (eventType)
        {
            case SDLEventType.Quit:
                evnt = new ApplicationQuitEvent();
                return true;

            case SDLEventType.WindowResized:
            case SDLEventType.WindowPixelSizeChanged:
                if (m_windows.TryGetValue(sdlEvent.Window.WindowID, out var resizedWindow))
                {
                    resizedWindow.UpdateSize(sdlEvent.Window.Data1, sdlEvent.Window.Data2);
                }

                evnt = new WindowResizeEvent(sdlEvent.Window.Data1, sdlEvent.Window.Data2);
                return true;

            case SDLEventType.WindowCloseRequested:
                if (m_windows.TryGetValue(sdlEvent.Window.WindowID, out var closingWindow))
                {
                    closingWindow.MarkClosed();
                }

                evnt = new WindowCloseEvent();
                return true;

            case SDLEventType.KeyDown:
            {
                var key = TranslateKey(sdlEvent.Key.Key);
                var modifiers = TranslateModifiers(sdlEvent.Key.Mod);
                evnt = new KeyPressedEvent(key, modifiers, sdlEvent.Key.Repeat != 0);
                return true;
            }

            case SDLEventType.KeyUp:
            {
                var key = TranslateKey(sdlEvent.Key.Key);
                var modifiers = TranslateModifiers(sdlEvent.Key.Mod);
                evnt = new KeyReleasedEvent(key, modifiers);
                return true;
            }

            case SDLEventType.MouseMotion:
                evnt = new MouseMovedEvent(sdlEvent.Motion.X, sdlEvent.Motion.Y);
                return true;

            case SDLEventType.MouseWheel:
            {
                var wheelX = sdlEvent.Wheel.X;
                var wheelY = sdlEvent.Wheel.Y;
                if (sdlEvent.Wheel.Direction == SDLMouseWheelDirection.Flipped)
                {
                    wheelX = -wheelX;
                    wheelY = -wheelY;
                }

                evnt = new MouseScrolledEvent(wheelX, wheelY);
                return true;
            }

            case SDLEventType.MouseButtonDown:
                if (TryTranslateMouseButton(sdlEvent.Button.Button, out var pressedButton))
                {
                    evnt = new MouseButtonPressedEvent(pressedButton);
                    return true;
                }

                break;

            case SDLEventType.MouseButtonUp:
                if (TryTranslateMouseButton(sdlEvent.Button.Button, out var releasedButton))
                {
                    evnt = new MouseButtonReleasedEvent(releasedButton);
                    return true;
                }

                break;
        }

        evnt = null;
        return false;
    }

    private static KeyModifier TranslateModifiers(ushort modifiers)
    {
        var sdlModifiers = (uint)modifiers;
        var result = KeyModifier.None;

        if ((sdlModifiers & (SDL.SDL_KMOD_LSHIFT | SDL.SDL_KMOD_RSHIFT)) != 0)
        {
            result |= KeyModifier.Shift;
        }

        if ((sdlModifiers & (SDL.SDL_KMOD_LCTRL | SDL.SDL_KMOD_RCTRL)) != 0)
        {
            result |= KeyModifier.Control;
        }

        if ((sdlModifiers & (SDL.SDL_KMOD_LALT | SDL.SDL_KMOD_RALT)) != 0)
        {
            result |= KeyModifier.Alt;
        }

        if ((sdlModifiers & (SDL.SDL_KMOD_LGUI | SDL.SDL_KMOD_RGUI)) != 0)
        {
            result |= KeyModifier.Super;
        }

        return result;
    }

    private static bool TryTranslateMouseButton(byte sdlButton, out MouseButton button)
    {
        switch (sdlButton)
        {
            case SDL.SDL_BUTTON_LEFT:
                button = MouseButton.Left;
                return true;
            case SDL.SDL_BUTTON_RIGHT:
                button = MouseButton.Right;
                return true;
            case SDL.SDL_BUTTON_MIDDLE:
                button = MouseButton.Middle;
                return true;
            case SDL.SDL_BUTTON_X1:
                button = MouseButton.XButton1;
                return true;
            case SDL.SDL_BUTTON_X2:
                button = MouseButton.XButton2;
                return true;
            default:
                button = MouseButton.Left;
                return false;
        }
    }

    private static KeyCode TranslateKey(int sdlKey)
    {
        return (uint)sdlKey switch
        {
            SDL.SDLK_A => KeyCode.A,
            SDL.SDLK_B => KeyCode.B,
            SDL.SDLK_C => KeyCode.C,
            SDL.SDLK_D => KeyCode.D,
            SDL.SDLK_E => KeyCode.E,
            SDL.SDLK_F => KeyCode.F,
            SDL.SDLK_G => KeyCode.G,
            SDL.SDLK_H => KeyCode.H,
            SDL.SDLK_I => KeyCode.I,
            SDL.SDLK_J => KeyCode.J,
            SDL.SDLK_K => KeyCode.K,
            SDL.SDLK_L => KeyCode.L,
            SDL.SDLK_M => KeyCode.M,
            SDL.SDLK_N => KeyCode.N,
            SDL.SDLK_O => KeyCode.O,
            SDL.SDLK_P => KeyCode.P,
            SDL.SDLK_Q => KeyCode.Q,
            SDL.SDLK_R => KeyCode.R,
            SDL.SDLK_S => KeyCode.S,
            SDL.SDLK_T => KeyCode.T,
            SDL.SDLK_U => KeyCode.U,
            SDL.SDLK_V => KeyCode.V,
            SDL.SDLK_W => KeyCode.W,
            SDL.SDLK_X => KeyCode.X,
            SDL.SDLK_Y => KeyCode.Y,
            SDL.SDLK_Z => KeyCode.Z,

            SDL.SDLK_0 => KeyCode.D0,
            SDL.SDLK_1 => KeyCode.D1,
            SDL.SDLK_2 => KeyCode.D2,
            SDL.SDLK_3 => KeyCode.D3,
            SDL.SDLK_4 => KeyCode.D4,
            SDL.SDLK_5 => KeyCode.D5,
            SDL.SDLK_6 => KeyCode.D6,
            SDL.SDLK_7 => KeyCode.D7,
            SDL.SDLK_8 => KeyCode.D8,
            SDL.SDLK_9 => KeyCode.D9,

            SDL.SDLK_ESCAPE => KeyCode.Escape,
            SDL.SDLK_SPACE => KeyCode.Space,
            SDL.SDLK_RETURN => KeyCode.Enter,
            SDL.SDLK_TAB => KeyCode.Tab,
            SDL.SDLK_BACKSPACE => KeyCode.Backspace,

            SDL.SDLK_LEFT => KeyCode.LeftArrow,
            SDL.SDLK_UP => KeyCode.UpArrow,
            SDL.SDLK_RIGHT => KeyCode.RightArrow,
            SDL.SDLK_DOWN => KeyCode.DownArrow,

            SDL.SDLK_LGUI => KeyCode.LeftSuper,
            SDL.SDLK_RGUI => KeyCode.RightSuper,
            SDL.SDLK_LSHIFT => KeyCode.LeftShift,
            SDL.SDLK_RSHIFT => KeyCode.RightShift,
            SDL.SDLK_LCTRL => KeyCode.LeftCtrl,
            SDL.SDLK_RCTRL => KeyCode.RightCtrl,
            SDL.SDLK_LALT => KeyCode.LeftAlt,
            SDL.SDLK_RALT => KeyCode.RightAlt,

            SDL.SDLK_CAPSLOCK => KeyCode.CapsLock,
            SDL.SDLK_INSERT => KeyCode.Insert,
            SDL.SDLK_DELETE => KeyCode.Delete,
            SDL.SDLK_HOME => KeyCode.Home,
            SDL.SDLK_END => KeyCode.End,
            SDL.SDLK_PAGEUP => KeyCode.PageUp,
            SDL.SDLK_PAGEDOWN => KeyCode.PageDown,

            SDL.SDLK_KP_0 => KeyCode.NumPad0,
            SDL.SDLK_KP_1 => KeyCode.NumPad1,
            SDL.SDLK_KP_2 => KeyCode.NumPad2,
            SDL.SDLK_KP_3 => KeyCode.NumPad3,
            SDL.SDLK_KP_4 => KeyCode.NumPad4,
            SDL.SDLK_KP_5 => KeyCode.NumPad5,
            SDL.SDLK_KP_6 => KeyCode.NumPad6,
            SDL.SDLK_KP_7 => KeyCode.NumPad7,
            SDL.SDLK_KP_8 => KeyCode.NumPad8,
            SDL.SDLK_KP_9 => KeyCode.NumPad9,

            SDL.SDLK_NUMLOCKCLEAR => KeyCode.NumLock,
            SDL.SDLK_SCROLLLOCK => KeyCode.ScrollLock,

            SDL.SDLK_F1 => KeyCode.F1,
            SDL.SDLK_F2 => KeyCode.F2,
            SDL.SDLK_F3 => KeyCode.F3,
            SDL.SDLK_F4 => KeyCode.F4,
            SDL.SDLK_F5 => KeyCode.F5,
            SDL.SDLK_F6 => KeyCode.F6,
            SDL.SDLK_F7 => KeyCode.F7,
            SDL.SDLK_F8 => KeyCode.F8,
            SDL.SDLK_F9 => KeyCode.F9,
            SDL.SDLK_F10 => KeyCode.F10,
            SDL.SDLK_F11 => KeyCode.F11,
            SDL.SDLK_F12 => KeyCode.F12,

            SDL.SDLK_PLUS => KeyCode.Plus,
            SDL.SDLK_COMMA => KeyCode.Comma,
            SDL.SDLK_MINUS => KeyCode.Minus,
            SDL.SDLK_PERIOD => KeyCode.Period,
            SDL.SDLK_SLASH => KeyCode.Slash,
            SDL.SDLK_GRAVE => KeyCode.Tilde,
            SDL.SDLK_BACKSLASH => KeyCode.Backslash,
            SDL.SDLK_SEMICOLON => KeyCode.Semicolon,
            SDL.SDLK_APOSTROPHE => KeyCode.Quote,
            SDL.SDLK_LEFTBRACKET => KeyCode.LeftBracket,
            SDL.SDLK_RIGHTBRACKET => KeyCode.RightBracket,
            _ => KeyCode.Unknown
        };
    }
}
