using System;

using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

/// <summary>Provides contextual state while constructing an editor menu.</summary>
public sealed class EditorMenuContext
{
    /// <summary>Creates a contextual menu request.</summary>
    /// <param name="editor">The shared passive editor context.</param>
    /// <param name="interactions">The active interaction entry point.</param>
    /// <param name="area">The exact menu area being requested.</param>
    /// <param name="target">The optional object the menu operates on.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="editor"/> or <paramref name="interactions"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="area"/> is empty.</exception>
    public EditorMenuContext(
        EditorContext editor,
        EditorInteractions interactions,
        EditorAreaId area,
        object? target = null)
    {
        this.editor = editor ?? throw new ArgumentNullException(nameof(editor));
        this.interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        this.area = area;
        this.target = target;
    }

    /// <summary>Gets the shared passive editor context.</summary>
    public EditorContext editor { get; }

    /// <summary>Gets the active interaction entry point.</summary>
    public EditorInteractions interactions { get; }

    /// <summary>Gets the requested menu area.</summary>
    public EditorAreaId area { get; }

    /// <summary>Gets the contextual menu target.</summary>
    public object? target { get; }

    internal EditorActionContext CreateActionContext(object? argument = null)
        => new(editor, interactions, area, target, argument);
}
