using System;

namespace Inno.Editor.Core.DragDrop;

/// <summary>Provides contextual state while beginning an editor drag.</summary>
public sealed class EditorDragContext
{
    /// <summary>Creates a drag context.</summary>
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
