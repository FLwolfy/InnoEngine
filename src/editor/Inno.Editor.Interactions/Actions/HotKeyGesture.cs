using System;

using Inno.Core.Events;
using Inno.Core.Input;

namespace Inno.Editor.Interactions;

/// <summary>Describes one keyboard gesture used to invoke an editor command.</summary>
public readonly record struct HotKeyGesture
{
    private static readonly KeyModifier S_PRIMARY_MODIFIER = OperatingSystem.IsMacOS()
        ? KeyModifier.Super
        : KeyModifier.Control;

    /// <summary>
    /// Creates a keyboard gesture with an exact set of modifier keys after symbolic-key normalization.
    /// </summary>
    /// <param name="key">The non-modifier key in the gesture.</param>
    /// <param name="modifiers">The modifier keys required by the gesture.</param>
    public HotKeyGesture(KeyCode key, KeyModifier modifiers = KeyModifier.None)
    {
        this.key = key;
        this.modifiers = modifiers;
    }

    /// <summary>Gets the main key.</summary>
    public KeyCode key { get; }

    /// <summary>Gets the required modifiers.</summary>
    public KeyModifier modifiers { get; }

    /// <summary>
    /// Creates a gesture that includes the platform primary modifier.
    /// </summary>
    /// <param name="key">The non-modifier key in the gesture.</param>
    /// <param name="additionalModifiers">Additional modifiers combined with Command on macOS or Control elsewhere.</param>
    /// <returns>A platform-aware keyboard gesture.</returns>
    public static HotKeyGesture Primary(KeyCode key, KeyModifier additionalModifiers = KeyModifier.None)
        => new(key, S_PRIMARY_MODIFIER | additionalModifiers);

    /// <summary>
    /// Formats the gesture as a human-readable editor menu shortcut label.
    /// </summary>
    /// <returns>A platform-aware textual representation of the gesture.</returns>
    public override string ToString()
    {
        string prefix = string.Empty;
        if ((modifiers & KeyModifier.Control) != 0)
            prefix += "Ctrl+";
        if ((modifiers & KeyModifier.Super) != 0)
            prefix += OperatingSystem.IsMacOS() ? "Cmd+" : "Super+";
        if ((modifiers & KeyModifier.Alt) != 0)
            prefix += "Alt+";
        if ((modifiers & KeyModifier.Shift) != 0)
            prefix += "Shift+";
        return prefix + key;
    }

    /// <summary>
    /// Returns whether a non-repeating key event matches this gesture after normalizing
    /// the physical Shift used to type a symbolic Plus key.
    /// </summary>
    /// <param name="keyEvent">The keyboard event to compare.</param>
    /// <returns><see langword="true"/> when the key and normalized modifiers match; otherwise, <see langword="false"/>.</returns>
    public bool Matches(KeyPressedEvent keyEvent)
        => !keyEvent.repeat &&
           keyEvent.key == key &&
           Normalize(key, keyEvent.modifiers) == Normalize(key, modifiers);

    private static KeyModifier Normalize(KeyCode key, KeyModifier value)
    {
        KeyModifier normalized = value &
                                 (KeyModifier.Alt |
                                  KeyModifier.Control |
                                  KeyModifier.Shift |
                                  KeyModifier.Super);
        return key == KeyCode.Plus
            ? normalized & ~KeyModifier.Shift
            : normalized;
    }
}
