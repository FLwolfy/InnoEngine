using System;

using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

/// <summary>Provides contextual state to an editor drop handler.</summary>
public sealed class EditorDropContext
{
    /// <summary>Creates a managed drop request.</summary>
    /// <param name="editor">The shared passive editor context.</param>
    /// <param name="interactions">The active interaction entry point.</param>
    /// <param name="area">The target interaction area.</param>
    /// <param name="data">The active managed drag data.</param>
    /// <param name="target">The managed target object exposed to typed handlers.</param>
    /// <param name="placement">The requested position relative to the target.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="editor"/>, <paramref name="interactions"/>, <paramref name="data"/>, or <paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="area"/> is empty.</exception>
    public EditorDropContext(
        EditorContext editor,
        EditorInteractions interactions,
        EditorAreaId area,
        EditorDragData data,
        object target,
        EditorDropPlacement placement = EditorDropPlacement.None)
    {
        this.editor = editor ?? throw new ArgumentNullException(nameof(editor));
        this.interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        this.area = area;
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        this.placement = placement;
    }

    /// <summary>Gets the shared passive editor context.</summary>
    public EditorContext editor { get; }

    /// <summary>Gets the active interaction entry point.</summary>
    public EditorInteractions interactions { get; }

    /// <summary>Gets the target interaction area.</summary>
    public EditorAreaId area { get; }

    /// <summary>Gets the active managed drag data.</summary>
    public EditorDragData data { get; }

    /// <summary>Gets the managed drop target.</summary>
    public object target { get; }

    /// <summary>Gets the requested placement relative to the target.</summary>
    public EditorDropPlacement placement { get; }
}
