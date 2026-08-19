using System;

using Inno.Core.Events;
using Inno.Core.Input;

namespace Inno.Editor.Core.Commands;

/// <summary>Describes one keyboard gesture used to invoke an editor command.</summary>
public readonly record struct HotKeyGesture
{
    private static readonly KeyModifier S_PRIMARY_MODIFIER = OperatingSystem.IsMacOS()
        ? KeyModifier.Super
        : KeyModifier.Control;

    /// <summary>Creates an exact keyboard gesture.</summary>
    public HotKeyGesture(KeyCode key, KeyModifier modifiers = KeyModifier.None)
    {
        this.key = key;
        this.modifiers = modifiers;
    }

    /// <summary>Gets the main key.</summary>
    public KeyCode key { get; }

    /// <summary>Gets the required modifiers.</summary>
    public KeyModifier modifiers { get; }

    /// <summary>Creates a gesture using the platform primary modifier.</summary>
    public static HotKeyGesture Primary(KeyCode key, KeyModifier additionalModifiers = KeyModifier.None)
        => new(key, S_PRIMARY_MODIFIER | additionalModifiers);

    /// <summary>Formats the gesture for an editor menu shortcut label.</summary>
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

    /// <summary>Returns whether a key event exactly matches this gesture.</summary>
    public bool Matches(KeyPressedEvent keyEvent)
        => !keyEvent.repeat && keyEvent.key == key && Normalize(keyEvent.modifiers) == Normalize(modifiers);

    private static KeyModifier Normalize(KeyModifier value)
        => value & (KeyModifier.Alt | KeyModifier.Control | KeyModifier.Shift | KeyModifier.Super);
}
