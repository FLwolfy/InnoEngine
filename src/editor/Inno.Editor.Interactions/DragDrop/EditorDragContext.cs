using System;

using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

/// <summary>Provides contextual state while beginning an editor drag.</summary>
public sealed class EditorDragContext
{
    /// <summary>Creates a managed editor drag request.</summary>
    /// <param name="editor">The shared passive editor context.</param>
    /// <param name="interactions">The active interaction entry point.</param>
    /// <param name="area">The interaction area that produced the source.</param>
    /// <param name="data">The managed source object, label, and validity predicate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="editor"/>, <paramref name="interactions"/>, or <paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="area"/> is empty.</exception>
    public EditorDragContext(
        EditorContext editor,
        EditorInteractions interactions,
        string area,
        EditorDragData data)
    {
        this.editor = editor ?? throw new ArgumentNullException(nameof(editor));
        this.interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        if (string.IsNullOrWhiteSpace(area))
            throw new ArgumentException("An editor interaction area is required.", nameof(area));
        this.area = area;
        this.data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>Gets the shared passive editor context.</summary>
    public EditorContext editor { get; }

    /// <summary>Gets the active interaction entry point.</summary>
    public EditorInteractions interactions { get; }

    /// <summary>Gets the source interaction area.</summary>
    public string area { get; }

    /// <summary>Gets the managed drag data.</summary>
    public EditorDragData data { get; }
}
