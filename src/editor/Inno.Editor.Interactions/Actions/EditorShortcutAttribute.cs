using System;

using Inno.Core.Input;

namespace Inno.Editor.Interactions;

/// <summary>Associates an editor action with a keyboard shortcut.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EditorShortcutAttribute : Attribute
{
    /// <summary>
    /// Creates a shortcut that can be dispatched from any focused interaction surface.
    /// </summary>
    /// <param name="key">The non-modifier key that triggers the action.</param>
    /// <param name="modifiers">The exact additional modifier keys required by the gesture.</param>
    /// <param name="primary">Whether the platform primary modifier, Command on macOS or Control elsewhere, is required.</param>
    public EditorShortcutAttribute(
        KeyCode key,
        KeyModifier modifiers = KeyModifier.None,
        bool primary = false)
        : this(string.Empty, key, modifiers, primary)
    {
    }

    /// <summary>
    /// Creates a shortcut scoped to an exact interaction area.
    /// </summary>
    /// <param name="area">The exact focused area that accepts the shortcut, or an empty string for any area.</param>
    /// <param name="key">The non-modifier key that triggers the action.</param>
    /// <param name="modifiers">The exact additional modifier keys required by the gesture.</param>
    /// <param name="primary">Whether the platform primary modifier, Command on macOS or Control elsewhere, is required.</param>
    public EditorShortcutAttribute(
        string area,
        KeyCode key,
        KeyModifier modifiers = KeyModifier.None,
        bool primary = false)
    {
        this.area = area ?? string.Empty;
        this.key = key;
        this.modifiers = modifiers;
        this.primary = primary;
    }

    /// <summary>Gets the optional exact interaction area.</summary>
    public string area { get; }

    /// <summary>Gets the shortcut key.</summary>
    public KeyCode key { get; }

    /// <summary>Gets additional shortcut modifiers.</summary>
    public KeyModifier modifiers { get; }

    /// <summary>Gets whether the platform primary modifier is required.</summary>
    public bool primary { get; }
}
