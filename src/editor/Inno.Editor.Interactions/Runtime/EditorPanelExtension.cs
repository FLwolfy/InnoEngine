using System;

using Inno.Editor.Core.Panels;

namespace Inno.Editor.Interactions.Runtime;

/// <summary>Describes one active dockable panel extension.</summary>
public sealed class EditorPanelExtension
{
    internal EditorPanelExtension(string id, string title, int order, EditorPanel panel)
    {
        this.id = id;
        this.title = title;
        this.order = order;
        this.panel = panel;
    }

    /// <summary>Gets the stable panel identifier.</summary>
    public string id { get; }

    /// <summary>Gets the visible panel title.</summary>
    public string title { get; }

    /// <summary>Gets the stable panel ordering value.</summary>
    public int order { get; }

    /// <summary>Gets the active panel instance.</summary>
    public EditorPanel panel { get; }
}
