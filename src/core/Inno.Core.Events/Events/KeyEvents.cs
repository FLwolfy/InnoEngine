using Inno.Core.Input;

namespace Inno.Core.Events;

/// <summary>
/// Base class for keyboard events.
/// </summary>
public abstract class KeyEvent(KeyCode key, KeyModifier modifiers = KeyModifier.None)
    : Event
{
    /// <summary>
    /// Gets the key code for this keyboard event.
    /// </summary>
    public KeyCode key { get; } = key;

    /// <summary>
    /// Gets active key modifiers for this keyboard event.
    /// </summary>
    public KeyModifier modifiers { get; } = modifiers;
}

/// <summary>
/// Raised when a key is pressed.
/// </summary>
public class KeyPressedEvent(
    KeyCode key,
    KeyModifier modifiers = KeyModifier.None,
    bool repeat = false)
    : KeyEvent(key, modifiers)
{
    /// <summary>
    /// Gets whether this key press is an auto-repeat event.
    /// </summary>
    public bool repeat { get; } = repeat;
}

/// <summary>
/// Raised when a key is released.
/// </summary>
public class KeyReleasedEvent(KeyCode key, KeyModifier modifiers = KeyModifier.None)
    : KeyEvent(key, modifiers)
{
}
