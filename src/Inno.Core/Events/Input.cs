using System;

namespace Inno.Core.Events;

public static class Input
{
    public enum MouseButton
    {
        Left = 0,
        Right = 1,
        Middle = 2,
        XButton1 = 3,
        XButton2 = 4
    }
    
    public enum MouseCursor
    {
        None = -1,
        
        Arrow = 0,
        TextInput = 1,
        ResizeAll = 2,
        ResizeNS = 3,
        ResizeEW = 4,
        ResizeNESW = 5,
        ResizeNWSE = 6,
        Hand = 7,
        NotAllowed = 8,
    }

    public enum KeyCode
    {
        Unknown = 0,
        
        A = 65,
        B = 66,
        C = 67,
        D = 68,
        E = 69,
        F = 70,
        G = 71,
        H = 72,
        I = 73,
        J = 74,
        K = 75,
        L = 76,
        M = 77,
        N = 78,
        O = 79,
        P = 80,
        Q = 81,
        R = 82,
        S = 83,
        T = 84,
        U = 85,
        V = 86,
        W = 87,
        X = 88,
        Y = 89,
        Z = 90,

        D0 = 48,
        D1 = 49,
        D2 = 50,
        D3 = 51,
        D4 = 52,
        D5 = 53,
        D6 = 54,
        D7 = 55,
        D8 = 56,
        D9 = 57,

        Escape = 27,
        Space = 32,
        Enter = 13,
        Tab = 9,
        Backspace = 8,

        LeftArrow = 37,
        UpArrow = 38,
        RightArrow = 39,
        DownArrow = 40,

        LeftSuper = 91, 
        RightSuper = 92,
        LeftShift = 160,
        RightShift = 161,
        LeftCtrl = 162,
        RightCtrl = 163,
        LeftAlt = 164,
        RightAlt = 165,

        CapsLock = 20,
        Insert = 45,
        Delete = 46,
        Home = 36,
        End = 35,
        PageUp = 33,
        PageDown = 34,

        NumPad0 = 96,
        NumPad1 = 97,
        NumPad2 = 98,
        NumPad3 = 99,
        NumPad4 = 100,
        NumPad5 = 101,
        NumPad6 = 102,
        NumPad7 = 103,
        NumPad8 = 104,
        NumPad9 = 105,

        NumLock = 144,
        ScrollLock = 145,

        F1 = 112,
        F2 = 113,
        F3 = 114,
        F4 = 115,
        F5 = 116,
        F6 = 117,
        F7 = 118,
        F8 = 119,
        F9 = 120,
        F10 = 121,
        F11 = 122,
        F12 = 123,

        Plus = 187,          // '+' key
        Comma = 188,         // ',' key
        Minus = 189,         // '-' key
        Period = 190,        // '.' key
        Slash = 191,         // '/' key
        Tilde = 192,         // '`' key
        Backslash = 220,     // '\' key
        Semicolon = 186,     // ';' key
        Quote = 222,         // ''' key
        LeftBracket = 219,   // '[' key
        RightBracket = 221   // ']' key
    }

    [Flags]
    public enum KeyModifier
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Super = 8,
    }

}