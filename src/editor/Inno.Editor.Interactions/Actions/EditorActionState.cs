namespace Inno.Editor.Interactions;

/// <summary>Describes the current presentation and availability of an editor action.</summary>
public readonly record struct EditorActionState
{
    /// <summary>
    /// Creates the contextual presentation state of an editor action.
    /// </summary>
    /// <param name="isVisible">Whether the action should be included in the current presentation.</param>
    /// <param name="isEnabled">Whether the action can execute in the current context.</param>
    /// <param name="isChecked">Whether the action is currently presented as checked.</param>
    /// <param name="displayName">An optional contextual label that replaces the static placement label.</param>
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
