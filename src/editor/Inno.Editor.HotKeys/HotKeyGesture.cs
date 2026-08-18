using System;

using Inno.Core.Events;
using Inno.Core.Input;

namespace Inno.Editor.HotKeys;

/// <summary>
/// Describes one keyboard gesture used to invoke an editor command.
/// </summary>
public readonly record struct HotKeyGesture
{
    private static readonly KeyModifier S_PRIMARY_MODIFIER = OperatingSystem.IsMacOS()
        ? KeyModifier.Super
        : KeyModifier.Control;

    /// <summary>
    /// Creates an exact keyboard gesture.
    /// </summary>
    /// <param name="key">Main key.</param>
    /// <param name="modifiers">Required modifiers.</param>
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
    /// Creates a gesture using the platform primary modifier: Command on macOS and Control elsewhere.
    /// </summary>
    /// <param name="key">Main key.</param>
    /// <param name="additionalModifiers">Additional required modifiers.</param>
    /// <returns>The platform-aware gesture.</returns>
    public static HotKeyGesture Primary(KeyCode key, KeyModifier additionalModifiers = KeyModifier.None)
        => new(key, S_PRIMARY_MODIFIER | additionalModifiers);

    internal bool Matches(KeyPressedEvent keyEvent)
        => !keyEvent.repeat && keyEvent.key == key && Normalize(keyEvent.modifiers) == Normalize(modifiers);

    private static KeyModifier Normalize(KeyModifier value)
        => value & (KeyModifier.Alt | KeyModifier.Control | KeyModifier.Shift | KeyModifier.Super);
}
