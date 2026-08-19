using System;

using Inno.Core.Input;

namespace Inno.Editor.Core.Commands;

/// <summary>Associates an editor action with a keyboard shortcut.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EditorShortcutAttribute : Attribute
{
    /// <summary>Creates a global shortcut.</summary>
    public EditorShortcutAttribute(
        KeyCode key,
        KeyModifier modifiers = KeyModifier.None,
        bool primary = false)
        : this(null, key, modifiers, primary)
    {
    }

    /// <summary>Creates a shortcut scoped to an interaction surface.</summary>
    public EditorShortcutAttribute(
        Type? surface,
        KeyCode key,
        KeyModifier modifiers = KeyModifier.None,
        bool primary = false)
    {
        this.surface = surface;
        this.key = key;
        this.modifiers = modifiers;
        this.primary = primary;
    }

    /// <summary>Gets the optional exact interaction surface.</summary>
    public Type? surface { get; }

    /// <summary>Gets the shortcut key.</summary>
    public KeyCode key { get; }

    /// <summary>Gets additional shortcut modifiers.</summary>
    public KeyModifier modifiers { get; }

    /// <summary>Gets whether the platform primary modifier is required.</summary>
    public bool primary { get; }
}
