namespace Inno.Editor.Core.DragDrop;

/// <summary>Describes the observable result of a completed editor drop.</summary>
public readonly record struct EditorDropResult
{
    /// <summary>Creates a completed drop result.</summary>
    public EditorDropResult(
        bool accepted,
        object? selectionTarget = null,
        object? revealTarget = null)
    {
        this.accepted = accepted;
        this.selectionTarget = selectionTarget;
        this.revealTarget = revealTarget;
    }

    /// <summary>Gets whether the drop was accepted.</summary>
    public bool accepted { get; }

    /// <summary>Gets the optional target that should become selected.</summary>
    public object? selectionTarget { get; }

    /// <summary>Gets the optional hierarchy target that should be revealed.</summary>
    public object? revealTarget { get; }

    /// <summary>Gets a rejected drop result.</summary>
    public static EditorDropResult rejected => new(false);

    /// <summary>Creates an accepted result.</summary>
    public static EditorDropResult Accepted(object? selectionTarget = null, object? revealTarget = null)
        => new(true, selectionTarget, revealTarget);
}
