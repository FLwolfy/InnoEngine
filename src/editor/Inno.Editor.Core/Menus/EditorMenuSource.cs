using System;

namespace Inno.Editor.Core.Menus;

/// <summary>Contributes dynamic entries to one or more editor menu surfaces.</summary>
public abstract class EditorMenuSource
{
    /// <summary>Adds dynamic entries for the supplied menu context.</summary>
    public abstract void Build(EditorMenuContext context, EditorMenuBuilder builder);
}
