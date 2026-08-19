using System;
using System.Collections.Generic;

namespace Inno.Editor.Core.Menus;

/// <summary>Contains a complete immutable menu tree.</summary>
public sealed class EditorMenuModel
{
    /// <summary>Creates a menu model.</summary>
    public EditorMenuModel(IReadOnlyList<EditorMenuItem>? items)
    {
        this.items = items ?? Array.Empty<EditorMenuItem>();
    }

    /// <summary>Gets root menu nodes in display order.</summary>
    public IReadOnlyList<EditorMenuItem> items { get; }
}
