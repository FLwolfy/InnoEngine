using Inno.Core.Input;

namespace Inno.Core.Events;

/// <summary>
/// Base class for keyboard events.
/// </summary>
public abstract class KeyEvent(uint windowId, KeyCode key, KeyModifier modifiers = KeyModifier.None)
    : Event
{
    /// <summary>
    /// Gets the source window id for this event.
    /// </summary>
    public uint windowId { get; } = windowId;

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
    uint windowId,
    KeyCode key,
    KeyModifier modifiers = KeyModifier.None,
    bool repeat = false)
    : KeyEvent(windowId, key, modifiers)
{
    /// <summary>
    /// Gets whether this key press is an auto-repeat event.
    /// </summary>
    public bool repeat { get; } = repeat;
}

/// <summary>
/// Raised when a key is released.
/// </summary>
public class KeyReleasedEvent(uint windowId, KeyCode key, KeyModifier modifiers = KeyModifier.None)
    : KeyEvent(windowId, key, modifiers)
{
}
