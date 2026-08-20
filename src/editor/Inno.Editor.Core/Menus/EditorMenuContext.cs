using System;

using Inno.Editor.Core.Commands;

namespace Inno.Editor.Core.Menus;

/// <summary>Provides contextual state while constructing an editor menu.</summary>
public sealed class EditorMenuContext
{
    /// <summary>
    /// Creates a contextual request used to collect and render a menu.
    /// </summary>
    /// <param name="editorContext">The shared editor context.</param>
    /// <param name="surface">The exact menu surface being requested.</param>
    /// <param name="target">The optional object the resulting menu operates on.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="editorContext"/> or <paramref name="surface"/> is <see langword="null"/>.</exception>
    public EditorMenuContext(EditorContext editorContext, Type surface, object? target = null)
    {
        this.editorContext = editorContext ?? throw new ArgumentNullException(nameof(editorContext));
        this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        this.target = target;
    }

    /// <summary>Gets the shared editor context.</summary>
    public EditorContext editorContext { get; }

    /// <summary>Gets the requested menu surface.</summary>
    public Type surface { get; }

    /// <summary>Gets the contextual menu target.</summary>
    public object? target { get; }

    /// <summary>
    /// Creates an action context that preserves this menu's editor, surface, and target.
    /// </summary>
    /// <param name="argument">An optional placement-specific argument for the action.</param>
    /// <returns>A new contextual action request for this menu.</returns>
    public EditorActionContext CreateActionContext(object? argument = null)
        => new(editorContext, surface, target, argument);
}
