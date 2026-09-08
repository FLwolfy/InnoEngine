using System.Collections.Generic;

namespace Inno.Editor.Interactions;

/// <summary>
/// Contains an immutable resolved toolbar for one interaction area.
/// </summary>
public sealed class EditorToolbarModel
{
    internal EditorToolbarModel(IReadOnlyList<EditorToolbarItem> items)
    {
        this.items = items;
    }

    /// <summary>
    /// Gets visible toolbar commands in deterministic display order.
    /// </summary>
    public IReadOnlyList<EditorToolbarItem> items { get; }
}
