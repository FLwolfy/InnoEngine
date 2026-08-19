namespace Inno.Editor.Core.DragDrop;

/// <summary>Describes whether and how a target accepts the active drag.</summary>
public readonly record struct EditorDropStatus
{
    /// <summary>Creates a drop compatibility status.</summary>
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

    /// <summary>Creates an accepted drop status.</summary>
    public static EditorDropStatus Accept(EditorDropVisual visual = EditorDropVisual.Highlight)
        => new(true, visual);
}
