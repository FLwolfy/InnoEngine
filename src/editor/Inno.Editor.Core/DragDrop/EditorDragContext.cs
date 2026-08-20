using System;

namespace Inno.Editor.Core.DragDrop;

/// <summary>Provides contextual state while beginning an editor drag.</summary>
public sealed class EditorDragContext
{
    /// <summary>
    /// Creates the contextual request used to begin a managed editor drag session.
    /// </summary>
    /// <param name="editorContext">The shared editor context.</param>
    /// <param name="surface">The interaction surface that produced the drag source.</param>
    /// <param name="data">The managed source object, label, and validity predicate.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null"/>.</exception>
    public EditorDragContext(EditorContext editorContext, Type surface, EditorDragData data)
    {
        this.editorContext = editorContext ?? throw new ArgumentNullException(nameof(editorContext));
        this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        this.data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>Gets the shared editor context.</summary>
    public EditorContext editorContext { get; }

    /// <summary>Gets the source interaction surface.</summary>
    public Type surface { get; }

    /// <summary>Gets the managed drag data.</summary>
    public EditorDragData data { get; }
}
