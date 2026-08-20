using System;

namespace Inno.Editor.Core.Menus;

/// <summary>Contributes dynamic entries to one or more editor menu surfaces.</summary>
public abstract class EditorMenuSource
{
    /// <summary>
    /// Adds context-dependent placements for the supplied menu request.
    /// </summary>
    /// <param name="context">The requested menu surface, target, and shared editor context.</param>
    /// <param name="builder">The collector that receives dynamic action placements.</param>
    public abstract void Build(EditorMenuContext context, EditorMenuBuilder builder);
}
