namespace Inno.Editor.Core.DragDrop;

/// <summary>Describes whether and how a target accepts the active drag.</summary>
public readonly record struct EditorDropStatus
{
    /// <summary>
    /// Creates the compatibility and presentation state of a potential drop target.
    /// </summary>
    /// <param name="canDrop">Whether the active source may be delivered to the target.</param>
    /// <param name="visual">The standard visual requested for the target.</param>
    public EditorDropStatus(bool canDrop, EditorDropVisual visual)
    {
        this.canDrop = canDrop;
        this.visual = visual;
    }

    /// <summary>Gets whether the active source may be dropped.</summary>
    public bool canDrop { get; }

    /// <summary>Gets the standard target visual.</summary>
    public EditorDropVisual visual { get; }

    /// <summary>Gets an incompatible drop status.</summary>
    public static EditorDropStatus rejected => new(false, EditorDropVisual.None);

    /// <summary>
    /// Creates an accepted drop status with the requested standard target visual.
    /// </summary>
    /// <param name="visual">The standard visual drawn while the compatible source hovers the target.</param>
    /// <returns>An accepted drop status.</returns>
    public static EditorDropStatus Accept(EditorDropVisual visual = EditorDropVisual.Highlight)
        => new(true, visual);
}
