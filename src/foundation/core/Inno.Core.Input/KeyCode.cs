namespace Inno.Core.Input;

/// <summary>
/// Represents a backend-neutral physical key used by runtime input and editor shortcut contracts.
/// </summary>
public enum KeyCode
{
    /// <summary>
    /// No recognized physical key.
    /// </summary>
    Unknown = 0,
    
    /// <summary>
    /// The A letter key.
    /// </summary>
    A = 65,
    /// <summary>
    /// The B letter key.
    /// </summary>
    B = 66,
    /// <summary>
    /// The C letter key.
    /// </summary>
    C = 67,
    /// <summary>
    /// The D letter key.
    /// </summary>
    D = 68,
    /// <summary>
    /// The E letter key.
    /// </summary>
    E = 69,
    /// <summary>
    /// The F letter key.
    /// </summary>
    F = 70,
    /// <summary>
    /// The G letter key.
    /// </summary>
    G = 71,
    /// <summary>
    /// The H letter key.
    /// </summary>
    H = 72,
    /// <summary>
    /// The I letter key.
    /// </summary>
    I = 73,
    /// <summary>
    /// The J letter key.
    /// </summary>
    J = 74,
    /// <summary>
    /// The K letter key.
    /// </summary>
    K = 75,
    /// <summary>
    /// The L letter key.
    /// </summary>
    L = 76,
    /// <summary>
    /// The M letter key.
    /// </summary>
    M = 77,
    /// <summary>
    /// The N letter key.
    /// </summary>
    N = 78,
    /// <summary>
    /// The O letter key.
    /// </summary>
    O = 79,
    /// <summary>
    /// The P letter key.
    /// </summary>
    P = 80,
    /// <summary>
    /// The Q letter key.
    /// </summary>
    Q = 81,
    /// <summary>
    /// The R letter key.
    /// </summary>
    R = 82,
    /// <summary>
    /// The S letter key.
    /// </summary>
    S = 83,
    /// <summary>
    /// The T letter key.
    /// </summary>
    T = 84,
    /// <summary>
    /// The U letter key.
    /// </summary>
    U = 85,
    /// <summary>
    /// The V letter key.
    /// </summary>
    V = 86,
    /// <summary>
    /// The W letter key.
    /// </summary>
    W = 87,
    /// <summary>
    /// The X letter key.
    /// </summary>
    X = 88,
    /// <summary>
    /// The Y letter key.
    /// </summary>
    Y = 89,
    /// <summary>
    /// The Z letter key.
    /// </summary>
    Z = 90,

    /// <summary>
    /// The 0 key on the primary keyboard row.
    /// </summary>
    D0 = 48,
    /// <summary>
    /// The 1 key on the primary keyboard row.
    /// </summary>
    D1 = 49,
    /// <summary>
    /// The 2 key on the primary keyboard row.
    /// </summary>
    D2 = 50,
    /// <summary>
    /// The 3 key on the primary keyboard row.
    /// </summary>
    D3 = 51,
    /// <summary>
    /// The 4 key on the primary keyboard row.
    /// </summary>
    D4 = 52,
    /// <summary>
    /// The 5 key on the primary keyboard row.
    /// </summary>
    D5 = 53,
    /// <summary>
    /// The 6 key on the primary keyboard row.
    /// </summary>
    D6 = 54,
    /// <summary>
    /// The 7 key on the primary keyboard row.
    /// </summary>
    D7 = 55,
    /// <summary>
    /// The 8 key on the primary keyboard row.
    /// </summary>
    D8 = 56,
    /// <summary>
    /// The 9 key on the primary keyboard row.
    /// </summary>
    D9 = 57,

    /// <summary>
    /// The escape key.
    /// </summary>
    Escape = 27,
    /// <summary>
    /// The space key.
    /// </summary>
    Space = 32,
    /// <summary>
    /// The enter key.
    /// </summary>
    Enter = 13,
    /// <summary>
    /// The tab key.
    /// </summary>
    Tab = 9,
    /// <summary>
    /// The backspace key.
    /// </summary>
    Backspace = 8,

    /// <summary>
    /// The left arrow key.
    /// </summary>
    LeftArrow = 37,
    /// <summary>
    /// The up arrow key.
    /// </summary>
    UpArrow = 38,
    /// <summary>
    /// The right arrow key.
    /// </summary>
    RightArrow = 39,
    /// <summary>
    /// The down arrow key.
    /// </summary>
    DownArrow = 40,

    /// <summary>
    /// The left super key.
    /// </summary>
    LeftSuper = 91, 
    /// <summary>
    /// The right super key.
    /// </summary>
    RightSuper = 92,
    /// <summary>
    /// The left shift key.
    /// </summary>
    LeftShift = 160,
    /// <summary>
    /// The right shift key.
    /// </summary>
    RightShift = 161,
    /// <summary>
    /// The left ctrl key.
    /// </summary>
    LeftCtrl = 162,
    /// <summary>
    /// The right ctrl key.
    /// </summary>
    RightCtrl = 163,
    /// <summary>
    /// The left alt key.
    /// </summary>
    LeftAlt = 164,
    /// <summary>
    /// The right alt key.
    /// </summary>
    RightAlt = 165,

    /// <summary>
    /// The caps lock key.
    /// </summary>
    CapsLock = 20,
    /// <summary>
    /// The insert key.
    /// </summary>
    Insert = 45,
    /// <summary>
    /// The delete key.
    /// </summary>
    Delete = 46,
    /// <summary>
    /// The home key.
    /// </summary>
    Home = 36,
    /// <summary>
    /// The end key.
    /// </summary>
    End = 35,
    /// <summary>
    /// The page up key.
    /// </summary>
    PageUp = 33,
    /// <summary>
    /// The page down key.
    /// </summary>
    PageDown = 34,

    /// <summary>
    /// The 0 key on the numeric keypad.
    /// </summary>
    NumPad0 = 96,
    /// <summary>
    /// The 1 key on the numeric keypad.
    /// </summary>
    NumPad1 = 97,
    /// <summary>
    /// The 2 key on the numeric keypad.
    /// </summary>
    NumPad2 = 98,
    /// <summary>
    /// The 3 key on the numeric keypad.
    /// </summary>
    NumPad3 = 99,
    /// <summary>
    /// The 4 key on the numeric keypad.
    /// </summary>
    NumPad4 = 100,
    /// <summary>
    /// The 5 key on the numeric keypad.
    /// </summary>
    NumPad5 = 101,
    /// <summary>
    /// The 6 key on the numeric keypad.
    /// </summary>
    NumPad6 = 102,
    /// <summary>
    /// The 7 key on the numeric keypad.
    /// </summary>
    NumPad7 = 103,
    /// <summary>
    /// The 8 key on the numeric keypad.
    /// </summary>
    NumPad8 = 104,
    /// <summary>
    /// The 9 key on the numeric keypad.
    /// </summary>
    NumPad9 = 105,

    /// <summary>
    /// The num lock key.
    /// </summary>
    NumLock = 144,
    /// <summary>
    /// The scroll lock key.
    /// </summary>
    ScrollLock = 145,

    /// <summary>
    /// The F1 function key.
    /// </summary>
    F1 = 112,
    /// <summary>
    /// The F2 function key.
    /// </summary>
    F2 = 113,
    /// <summary>
    /// The F3 function key.
    /// </summary>
    F3 = 114,
    /// <summary>
    /// The F4 function key.
    /// </summary>
    F4 = 115,
    /// <summary>
    /// The F5 function key.
    /// </summary>
    F5 = 116,
    /// <summary>
    /// The F6 function key.
    /// </summary>
    F6 = 117,
    /// <summary>
    /// The F7 function key.
    /// </summary>
    F7 = 118,
    /// <summary>
    /// The F8 function key.
    /// </summary>
    F8 = 119,
    /// <summary>
    /// The F9 function key.
    /// </summary>
    F9 = 120,
    /// <summary>
    /// The F10 function key.
    /// </summary>
    F10 = 121,
    /// <summary>
    /// The F11 function key.
    /// </summary>
    F11 = 122,
    /// <summary>
    /// The F12 function key.
    /// </summary>
    F12 = 123,

    /// <summary>
    /// The plus key.
    /// </summary>
    Plus = 187,          // '+' key
    /// <summary>
    /// The comma key.
    /// </summary>
    Comma = 188,         // ',' key
    /// <summary>
    /// The minus key.
    /// </summary>
    Minus = 189,         // '-' key
    /// <summary>
    /// The period key.
    /// </summary>
    Period = 190,        // '.' key
    /// <summary>
    /// The slash key.
    /// </summary>
    Slash = 191,         // '/' key
    /// <summary>
    /// The tilde key.
    /// </summary>
    Tilde = 192,         // '`' key
    /// <summary>
    /// The backslash key.
    /// </summary>
    Backslash = 220,     // '\' key
    /// <summary>
    /// The semicolon key.
    /// </summary>
    Semicolon = 186,     // ';' key
    /// <summary>
    /// The quote key.
    /// </summary>
    Quote = 222,         // ''' key
    /// <summary>
    /// The left bracket key.
    /// </summary>
    LeftBracket = 219,   // '[' key
    /// <summary>
    /// The right bracket key.
    /// </summary>
    RightBracket = 221   // ']' key
}