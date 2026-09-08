using System;
using System.Collections.Generic;

namespace Inno.Editor.Interactions;

/// <summary>
/// Contains a complete immutable menu tree.
/// </summary>
public sealed class EditorMenuModel
{
    /// <summary>
    /// Creates a complete immutable menu tree from resolved root nodes.
    /// </summary>
    /// <param name="items">
    /// The root nodes in stable display order, or <see langword="null"/> for an empty menu.
    /// </param>
    public EditorMenuModel(IReadOnlyList<EditorMenuItem>? items)
    {
        this.items = items ?? Array.Empty<EditorMenuItem>();
    }

    /// <summary>
    /// Gets root menu nodes in display order.
    /// </summary>
    public IReadOnlyList<EditorMenuItem> items { get; }
}
