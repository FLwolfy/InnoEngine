using Inno.Core.Input;

namespace Inno.Core.Events;

public abstract class KeyEvent(KeyCode key, KeyModifier modifiers = KeyModifier.None)
    : Event
{
    public KeyCode key { get; } = key;
    public KeyModifier modifiers { get; } = modifiers;

    public sealed override EventCategory category => EventCategory.Input | EventCategory.Keyboard;
}

public class KeyPressedEvent(
    KeyCode key,
    KeyModifier modifiers = KeyModifier.None,
    bool repeat = false)
    : KeyEvent(key, modifiers)
{
    public bool repeat { get; } = repeat;

    public override EventType type => EventType.KeyPressed;
}

public class KeyReleasedEvent(KeyCode key, KeyModifier modifiers = KeyModifier.None)
    : KeyEvent(key, modifiers)
{
    public override EventType type => EventType.KeyReleased;
}