using System;

namespace Inno.Editor.Core.Panels;

/// <summary>Registers an editor panel for automatic discovery.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EditorPanelAttribute : Attribute
{
    /// <summary>Creates an editor panel registration.</summary>
    public EditorPanelAttribute(
        string id,
        string title,
        int order = 0,
        bool defaultOpen = true)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("An editor panel identifier is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("An editor panel title is required.", nameof(title));
        this.id = id;
        this.title = title;
        this.order = order;
        this.defaultOpen = defaultOpen;
    }

    /// <summary>Gets the stable panel identifier.</summary>
    public string id { get; }

    /// <summary>Gets the visible panel title.</summary>
    public string title { get; }

    /// <summary>Gets the stable panel ordering value.</summary>
    public int order { get; }

    /// <summary>Gets whether a newly discovered panel is open by default.</summary>
    public bool defaultOpen { get; }
}
