using System;

namespace Inno.Editor.Core.DragDrop;

/// <summary>Provides contextual state to an editor drop handler.</summary>
public sealed class EditorDropContext
{
    /// <summary>
    /// Creates the contextual request used to query or deliver a managed drop.
    /// </summary>
    /// <param name="editorContext">The shared editor context.</param>
    /// <param name="surface">The target interaction surface.</param>
    /// <param name="data">The active managed drag data.</param>
    /// <param name="target">The managed target object exposed to typed handlers.</param>
    /// <param name="placement">The requested position relative to the target.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="editorContext"/>, <paramref name="surface"/>, <paramref name="data"/>, or <paramref name="target"/> is <see langword="null"/>.</exception>
    public EditorDropContext(
        EditorContext editorContext,
        Type surface,
        EditorDragData data,
        object target,
        EditorDropPlacement placement = EditorDropPlacement.None)
    {
        this.editorContext = editorContext ?? throw new ArgumentNullException(nameof(editorContext));
        this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        this.placement = placement;
    }

    /// <summary>Gets the shared editor context.</summary>
    public EditorContext editorContext { get; }

    /// <summary>Gets the target interaction surface.</summary>
    public Type surface { get; }

    /// <summary>Gets the active managed drag data.</summary>
    public EditorDragData data { get; }

    /// <summary>Gets the managed drop target.</summary>
    public object target { get; }

    /// <summary>Gets the requested placement relative to the target.</summary>
    public EditorDropPlacement placement { get; }
}
