using System;

using Inno.Editor.Core.Commands;

namespace Inno.Editor.Core.Menus;

/// <summary>Provides contextual state while constructing an editor menu.</summary>
public sealed class EditorMenuContext
{
    /// <summary>Creates a menu context.</summary>
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

    /// <summary>Creates a command context for this menu.</summary>
    public EditorActionContext CreateActionContext(object? argument = null)
        => new(editorContext, surface, target, argument);
}
