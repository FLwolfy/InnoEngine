namespace Inno.Editor.Core.Commands;

/// <summary>Describes the current presentation and availability of an editor action.</summary>
public readonly record struct EditorActionState
{
    /// <summary>Creates an action state.</summary>
    public EditorActionState(
        bool isVisible,
        bool isEnabled,
        bool isChecked = false,
        string? displayName = null)
    {
        this.isVisible = isVisible;
        this.isEnabled = isEnabled;
        this.isChecked = isChecked;
        this.displayName = displayName;
    }

    /// <summary>Gets whether the action should be displayed.</summary>
    public bool isVisible { get; }

    /// <summary>Gets whether the action can execute.</summary>
    public bool isEnabled { get; }

    /// <summary>Gets whether the action is currently checked.</summary>
    public bool isChecked { get; }

    /// <summary>Gets an optional contextual display name.</summary>
    public string? displayName { get; }

    /// <summary>Gets a visible and enabled action state.</summary>
    public static EditorActionState enabled => new(true, true);

    /// <summary>Gets a visible but disabled action state.</summary>
    public static EditorActionState disabled => new(true, false);

    /// <summary>Gets a hidden action state.</summary>
    public static EditorActionState hidden => new(false, false);
}
