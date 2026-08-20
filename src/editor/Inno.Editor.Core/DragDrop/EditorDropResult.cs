namespace Inno.Editor.Core.DragDrop;

/// <summary>Describes the observable result of a completed editor drop.</summary>
public readonly record struct EditorDropResult
{
    /// <summary>
    /// Creates the observable result of a completed drop operation.
    /// </summary>
    /// <param name="accepted">Whether the handler accepted and completed the drop.</param>
    /// <param name="selectionTarget">An optional object that the originating view should select.</param>
    /// <param name="revealTarget">An optional object that the originating view should reveal or expand.</param>
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

    /// <summary>
    /// Creates an accepted drop result with optional presentation requests.
    /// </summary>
    /// <param name="selectionTarget">An optional object that the originating view should select.</param>
    /// <param name="revealTarget">An optional object that the originating view should reveal or expand.</param>
    /// <returns>An accepted drop result containing the supplied presentation requests.</returns>
    public static EditorDropResult Accepted(object? selectionTarget = null, object? revealTarget = null)
        => new(true, selectionTarget, revealTarget);
}
