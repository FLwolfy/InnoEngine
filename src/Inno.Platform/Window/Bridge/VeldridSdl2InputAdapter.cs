using System;
using Inno.Core.Events;

using Veldrid;

using SYSVector2 = System.Numerics.Vector2;

namespace Inno.Platform.Window.Bridge;

internal static class VeldridSdl2InputAdapter
{
    private static SYSVector2? m_lastMousePos;
    
    public static void AdaptInputEvents(InputSnapshot snapshot, Action<Event> onEvent)
    {
        // Key
        foreach (var keyEvent in snapshot.KeyEvents)
        {
            var key = ConvertKey(keyEvent.Key);
            if (key == Input.KeyCode.Unknown) continue;

            var modifiers = ConvertKeyModifiers(keyEvent.Modifiers);
            if (keyEvent.Down)
                onEvent(new KeyPressedEvent(key, modifiers, keyEvent.Repeat));
            else
                onEvent(new KeyReleasedEvent(key));
        }

        // MouseButton
        foreach (var mouseEvent in snapshot.MouseEvents)
        {
            var btn = ConvertMouseButton(mouseEvent.MouseButton);
            if (mouseEvent.Down)
                onEvent(new MouseButtonPressedEvent(btn));
            else
                onEvent(new MouseButtonReleasedEvent(btn));
        }

        // MouseMove
        if (m_lastMousePos != snapshot.MousePosition)
        {
            m_lastMousePos = snapshot.MousePosition;
            onEvent(new MouseMovedEvent(snapshot.MousePosition.X, snapshot.MousePosition.Y));
        }

        // MouseScroll
        if (snapshot.WheelDelta != 0)
            onEvent(new MouseScrolledEvent(0, snapshot.WheelDelta));
    }

    private static Input.MouseButton ConvertMouseButton(MouseButton veldridButton)
    {
        return veldridButton switch
        {
            MouseButton.Left => Input.MouseButton.Left,
            MouseButton.Right => Input.MouseButton.Right,
            MouseButton.Middle => Input.MouseButton.Middle,
            MouseButton.Button1 => Input.MouseButton.XButton1,
            MouseButton.Button2 => Input.MouseButton.XButton2,
            _ => Input.MouseButton.Left
        };
    }

    private static Input.KeyCode ConvertKey(Key key)
    {
        return key switch
        {
            // Letters
            Key.A => Input.KeyCode.A,
            Key.B => Input.KeyCode.B,
            Key.C => Input.KeyCode.C,
            Key.D => Input.KeyCode.D,
            Key.E => Input.KeyCode.E,
            Key.F => Input.KeyCode.F,
            Key.G => Input.KeyCode.G,
            Key.H => Input.KeyCode.H,
            Key.I => Input.KeyCode.I,
            Key.J => Input.KeyCode.J,
            Key.K => Input.KeyCode.K,
            Key.L => Input.KeyCode.L,
            Key.M => Input.KeyCode.M,
            Key.N => Input.KeyCode.N,
            Key.O => Input.KeyCode.O,
            Key.P => Input.KeyCode.P,
            Key.Q => Input.KeyCode.Q,
            Key.R => Input.KeyCode.R,
            Key.S => Input.KeyCode.S,
            Key.T => Input.KeyCode.T,
            Key.U => Input.KeyCode.U,
            Key.V => Input.KeyCode.V,
            Key.W => Input.KeyCode.W,
            Key.X => Input.KeyCode.X,
            Key.Y => Input.KeyCode.Y,
            Key.Z => Input.KeyCode.Z,

            // Numbers
            Key.Number0 => Input.KeyCode.D0,
            Key.Number1 => Input.KeyCode.D1,
            Key.Number2 => Input.KeyCode.D2,
            Key.Number3 => Input.KeyCode.D3,
            Key.Number4 => Input.KeyCode.D4,
            Key.Number5 => Input.KeyCode.D5,
            Key.Number6 => Input.KeyCode.D6,
            Key.Number7 => Input.KeyCode.D7,
            Key.Number8 => Input.KeyCode.D8,
            Key.Number9 => Input.KeyCode.D9,

            // Function Keys
            Key.F1 => Input.KeyCode.F1,
            Key.F2 => Input.KeyCode.F2,
            Key.F3 => Input.KeyCode.F3,
            Key.F4 => Input.KeyCode.F4,
            Key.F5 => Input.KeyCode.F5,
            Key.F6 => Input.KeyCode.F6,
            Key.F7 => Input.KeyCode.F7,
            Key.F8 => Input.KeyCode.F8,
            Key.F9 => Input.KeyCode.F9,
            Key.F10 => Input.KeyCode.F10,
            Key.F11 => Input.KeyCode.F11,
            Key.F12 => Input.KeyCode.F12,

            // Controls
            Key.Space => Input.KeyCode.Space,
            Key.Enter => Input.KeyCode.Enter,
            Key.Tab => Input.KeyCode.Tab,
            Key.BackSpace => Input.KeyCode.Backspace,
            Key.Escape => Input.KeyCode.Escape,
            Key.Insert => Input.KeyCode.Insert,
            Key.Delete => Input.KeyCode.Delete,
            Key.Home => Input.KeyCode.Home,
            Key.End => Input.KeyCode.End,
            Key.PageUp => Input.KeyCode.PageUp,
            Key.PageDown => Input.KeyCode.PageDown,

            // Arrows
            Key.Left => Input.KeyCode.LeftArrow,
            Key.Right => Input.KeyCode.RightArrow,
            Key.Up => Input.KeyCode.UpArrow,
            Key.Down => Input.KeyCode.DownArrow,

            // Modifiers
            Key.LWin => Input.KeyCode.LeftSuper,
            Key.RWin => Input.KeyCode.RightSuper,
            Key.LShift => Input.KeyCode.LeftShift,
            Key.RShift => Input.KeyCode.RightShift,
            Key.LControl => Input.KeyCode.LeftCtrl,
            Key.RControl => Input.KeyCode.RightCtrl,
            Key.LAlt => Input.KeyCode.LeftAlt,
            Key.RAlt => Input.KeyCode.RightAlt,
            Key.CapsLock => Input.KeyCode.CapsLock,
            Key.ScrollLock => Input.KeyCode.ScrollLock,
            Key.NumLock => Input.KeyCode.NumLock,

            // Numpad
            Key.Keypad0 => Input.KeyCode.NumPad0,
            Key.Keypad1 => Input.KeyCode.NumPad1,
            Key.Keypad2 => Input.KeyCode.NumPad2,
            Key.Keypad3 => Input.KeyCode.NumPad3,
            Key.Keypad4 => Input.KeyCode.NumPad4,
            Key.Keypad5 => Input.KeyCode.NumPad5,
            Key.Keypad6 => Input.KeyCode.NumPad6,
            Key.Keypad7 => Input.KeyCode.NumPad7,
            Key.Keypad8 => Input.KeyCode.NumPad8,
            Key.Keypad9 => Input.KeyCode.NumPad9,

            // Symbols
            Key.Plus => Input.KeyCode.Plus,
            Key.Minus => Input.KeyCode.Minus,
            Key.Comma => Input.KeyCode.Comma,
            Key.Period => Input.KeyCode.Period,
            Key.Slash => Input.KeyCode.Slash,
            Key.BackSlash => Input.KeyCode.Backslash,
            Key.Semicolon => Input.KeyCode.Semicolon,
            Key.Quote => Input.KeyCode.Quote,
            Key.BracketLeft => Input.KeyCode.LeftBracket,
            Key.BracketRight => Input.KeyCode.RightBracket,
            Key.Grave => Input.KeyCode.Tilde,

            _ => Input.KeyCode.Unknown
        };
    }

    private static Input.KeyModifier ConvertKeyModifiers(ModifierKeys modifiers)
    {
        return (Input.KeyModifier) (int) modifiers;
    }
}