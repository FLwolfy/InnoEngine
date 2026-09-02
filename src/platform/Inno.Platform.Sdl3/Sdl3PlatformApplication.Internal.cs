using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Inno.Core.Events;
using Inno.Core.Input;
using Inno.Native.Sdl3;
using Inno.Platform;

namespace Inno.Platform.Sdl3;

/// <summary>
/// Platform runtime entry point responsible for window creation and platform event polling.
/// </summary>
public sealed partial class Sdl3PlatformApplication
{
    private readonly List<Event> m_pendingEvents = [];
    private readonly Dictionary<uint, Sdl3PlatformWindow> m_windows = [];
    private readonly List<ISdl3ApplicationExtension> m_extensions = [];
    private SDLEventFilter? m_liveResizeEventWatch;
    private SDLMainThreadCallback? m_liveResizeMainThreadCallback;
    private GCHandle m_liveResizeEventWatchHandle;
    private int m_liveResizeRedrawQueued;
    private uint m_liveResizeWindowId;
    private int m_pendingEventReadIndex;
    private bool m_disposed;

    private unsafe void Initialize()
    {
        // Deliver the activating mouse press to the application as well as focusing the window.
        // This is required for first-click widgets, double-clicks, and drags across editor viewports.
        _ = SDL.SetHint(SDL.SDL_HINT_MOUSE_FOCUS_CLICKTHROUGH, "1");
        _ = SDL.SetHint(SDL.SDL_HINT_MOUSE_AUTO_CAPTURE, "0");

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

    /// <summary>
    /// Creates and validates a caller-owned window value.
    /// </summary>
    /// <param name="options">
    /// The validated configuration that controls this operation.
    /// </param>
    /// <returns>
    /// The validated sdl3platform window that represents the completed operation.
    /// </returns>
    public partial Sdl3PlatformWindow CreateWindow(PlatformWindowOptions options)
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

        var window = new Sdl3PlatformWindow(windowHandle, options.title);
        m_windows[window.windowId] = window;
        return window;
    }

    /// <summary>
    /// Attempts to dequeue the next backend-neutral platform event.
    /// </summary>
    /// <param name="evnt">
    /// The evnt consumed by poll event; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public partial bool PollEvent(out Event? evnt)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        if (TryDequeuePendingEvent(out evnt))
        {
            return true;
        }

        PollAndCoalescePendingEvents();
        if (TryDequeuePendingEvent(out evnt))
        {
            return true;
        }

        evnt = null;
        return false;
    }

    /// <summary>
    /// Retrieves the requested windows value from current authoritative state.
    /// </summary>
    /// <returns>
    /// An immutable snapshot of the values selected by the operation.
    /// </returns>
    public partial IReadOnlyList<Sdl3PlatformWindow> GetWindows()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        return GetWindowsCore();
    }

    private unsafe IReadOnlyList<Sdl3PlatformWindow> GetWindowsCore()
    {

        var count = 0;
        var nativeWindows = SDL.GetWindows(ref count);
        if (nativeWindows.IsNull || count <= 0)
        {
            return Array.Empty<Sdl3PlatformWindow>();
        }

        try
        {
            var windows = new List<Sdl3PlatformWindow>(count);
            for (var i = 0; i < count; i++)
            {
                var nativeWindow = nativeWindows[i];
                if (nativeWindow == null)
                {
                    continue;
                }

                var nativeWindowPtr = new SDLWindowPtr(nativeWindow);
                var windowId = SDL.GetWindowID(nativeWindowPtr);
                if (m_windows.TryGetValue(windowId, out var existingWindow))
                {
                    windows.Add(existingWindow);
                    continue;
                }

                // This includes foreign windows managed by integrations, e.g. ImGui viewports.
                var title = SDL.GetWindowTitleS(nativeWindowPtr);
                windows.Add(new Sdl3PlatformWindow(nativeWindowPtr, title, ownsNativeWindow: false));
            }

            return windows;
        }
        finally
        {
            SDL.Free((void*)nativeWindows.Handle);
        }
    }

    /// <summary>
    /// Registers a platform extension for the application lifetime.
    /// </summary>
    /// <param name="extension">
    /// The extension consumed by register extension; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated idisposable that represents the completed operation.
    /// </returns>
    public partial IDisposable RegisterExtension(ISdl3ApplicationExtension extension)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(extension);
        if (m_extensions.Contains(extension))
            throw new InvalidOperationException("The platform extension is already registered.");
        m_extensions.Add(extension);
        return new ExtensionRegistration(this, extension);
    }

    private bool TryDequeuePendingEvent(out Event? evnt)
    {
        if (m_pendingEventReadIndex >= m_pendingEvents.Count)
        {
            evnt = null;
            return false;
        }

        evnt = m_pendingEvents[m_pendingEventReadIndex++];
        if (m_pendingEventReadIndex >= m_pendingEvents.Count)
        {
            m_pendingEvents.Clear();
            m_pendingEventReadIndex = 0;
        }

        return true;
    }

    private void PollAndCoalescePendingEvents()
    {
        m_pendingEvents.Clear();
        m_pendingEventReadIndex = 0;

        Dictionary<PendingEventCoalesceKey, int> latestEventIndicesByKey = new();
        SDLEvent sdlEvent = default;
        while (SDL.PollEvent(ref sdlEvent))
        {
            DispatchNativeEvent(ref sdlEvent);

            if (!TryTranslateEvent(ref sdlEvent, out var translatedEvent) || translatedEvent is null)
            {
                continue;
            }

            if (TryCreatePendingEventCoalesceKey(translatedEvent, out var key)
                && latestEventIndicesByKey.TryGetValue(key, out var index))
            {
                m_pendingEvents[index] = translatedEvent;
                continue;
            }

            var nextIndex = m_pendingEvents.Count;
            m_pendingEvents.Add(translatedEvent);
            if (TryCreatePendingEventCoalesceKey(translatedEvent, out key))
            {
                latestEventIndicesByKey[key] = nextIndex;
            }
        }
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

        if (m_liveResizeEventWatch is not null && m_liveResizeEventWatchHandle.IsAllocated)
        {
            var userData = (nint)GCHandle.ToIntPtr(m_liveResizeEventWatchHandle);
            SDL.RemoveEventWatch(m_liveResizeEventWatch, userData);
            m_liveResizeEventWatchHandle.Free();
            m_liveResizeEventWatch = null;
            m_liveResizeMainThreadCallback = null;
        }

        foreach (ISdl3ApplicationExtension extension in m_extensions.ToArray())
            extension.OnApplicationDisposing(this);
        m_extensions.Clear();

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
        if (handle.Target is not Sdl3PlatformApplication application || application.m_disposed)
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
        if (handle.Target is not Sdl3PlatformApplication application || application.m_disposed)
        {
            return;
        }

        var windowId = Volatile.Read(ref application.m_liveResizeWindowId);
        Interlocked.Exchange(ref application.m_liveResizeRedrawQueued, 0);
        if (windowId != 0)
        {
            application.DispatchLiveResizeRedraw(windowId);
        }
    }

    private unsafe void DispatchNativeEvent(ref SDLEvent sdlEvent)
    {
        ReadOnlySpan<byte> nativeEventData = new(
            Unsafe.AsPointer(ref sdlEvent),
            sizeof(SDLEvent));
        foreach (ISdl3ApplicationExtension extension in m_extensions.ToArray())
            extension.ProcessNativeEvent(this, nativeEventData);
    }

    private void DispatchLiveResizeRedraw(uint windowId)
    {
        foreach (ISdl3ApplicationExtension extension in m_extensions.ToArray())
            extension.RenderLiveResizeWindow(this, windowId);
    }

    private void UnregisterExtension(ISdl3ApplicationExtension extension)
        => m_extensions.Remove(extension);

    private sealed class ExtensionRegistration(
        Sdl3PlatformApplication application,
        ISdl3ApplicationExtension extension) : IDisposable
    {
        private Sdl3PlatformApplication? m_application = application;
        private ISdl3ApplicationExtension? m_extension = extension;

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            Sdl3PlatformApplication? owner = Interlocked.Exchange(ref m_application, null);
            ISdl3ApplicationExtension? registered = Interlocked.Exchange(ref m_extension, null);
            if (owner is not null && registered is not null)
                owner.UnregisterExtension(registered);
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
                if (m_windows.TryGetValue(sdlEvent.Window.WindowID, out var resizedWindow))
                {
                    resizedWindow.UpdateLogicalSize(sdlEvent.Window.Data1, sdlEvent.Window.Data2);
                }

                evnt = new WindowResizeEvent(sdlEvent.Window.WindowID, sdlEvent.Window.Data1, sdlEvent.Window.Data2);
                return true;

            case SDLEventType.WindowPixelSizeChanged:
                if (m_windows.TryGetValue(sdlEvent.Window.WindowID, out var pixelResizedWindow))
                {
                    pixelResizedWindow.UpdatePixelSize(sdlEvent.Window.Data1, sdlEvent.Window.Data2);
                    evnt = new WindowResizeEvent(
                        sdlEvent.Window.WindowID,
                        pixelResizedWindow.width,
                        pixelResizedWindow.height);
                    return true;
                }

                evnt = null;
                return false;

            case SDLEventType.WindowCloseRequested:
                if (m_windows.TryGetValue(sdlEvent.Window.WindowID, out var closingWindow))
                {
                    closingWindow.MarkClosed();
                }

                evnt = new WindowCloseEvent(sdlEvent.Window.WindowID);
                return true;

            case SDLEventType.WindowFocusGained:
            case SDLEventType.WindowFocusLost:
            {
                bool isFocused = eventType == SDLEventType.WindowFocusGained;
                if (m_windows.TryGetValue(sdlEvent.Window.WindowID, out Sdl3PlatformWindow? focusedWindow))
                    focusedWindow.UpdateFocus(isFocused);
                evnt = new WindowFocusChangedEvent(sdlEvent.Window.WindowID, isFocused);
                return true;
            }

            case SDLEventType.KeyDown:
            {
                var key = TranslateKey(sdlEvent.Key.Key);
                var modifiers = TranslateModifiers(sdlEvent.Key.Mod);
                evnt = new KeyPressedEvent(sdlEvent.Key.WindowID, key, modifiers, sdlEvent.Key.Repeat != 0);
                return true;
            }

            case SDLEventType.KeyUp:
            {
                var key = TranslateKey(sdlEvent.Key.Key);
                var modifiers = TranslateModifiers(sdlEvent.Key.Mod);
                evnt = new KeyReleasedEvent(sdlEvent.Key.WindowID, key, modifiers);
                return true;
            }

            case SDLEventType.MouseMotion:
                evnt = new MouseMovedEvent(sdlEvent.Motion.WindowID, sdlEvent.Motion.X, sdlEvent.Motion.Y);
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

                evnt = new MouseScrolledEvent(sdlEvent.Wheel.WindowID, wheelX, wheelY);
                return true;
            }

            case SDLEventType.MouseButtonDown:
                if (TryTranslateMouseButton(sdlEvent.Button.Button, out var pressedButton))
                {
                    evnt = new MouseButtonPressedEvent(sdlEvent.Button.WindowID, pressedButton);
                    return true;
                }

                break;

            case SDLEventType.MouseButtonUp:
                if (TryTranslateMouseButton(sdlEvent.Button.Button, out var releasedButton))
                {
                    evnt = new MouseButtonReleasedEvent(sdlEvent.Button.WindowID, releasedButton);
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
            SDL.SDLK_EQUALS => KeyCode.Plus,
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

    private static bool TryCreatePendingEventCoalesceKey(Event evnt, out PendingEventCoalesceKey key)
    {
        if (evnt is WindowEvent windowEvent)
        {
            key = new PendingEventCoalesceKey(windowEvent.windowId, evnt.GetType());
            return true;
        }

        if (evnt is ApplicationEvent)
        {
            key = new PendingEventCoalesceKey(0, evnt.GetType());
            return true;
        }

        key = default;
        return false;
    }

    private readonly record struct PendingEventCoalesceKey(uint windowId, Type eventType);
}
