using System;

namespace Inno.Editor.Core;

/// <summary>Registers an editor panel for automatic discovery.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EditorPanelAttribute : Attribute
{
    /// <summary>
    /// Creates a dockable panel registration with stable identity and presentation metadata.
    /// </summary>
    /// <param name="id">The stable identity used for Panel-menu routing and reload-state retention.</param>
    /// <param name="title">The visible dockable-window title.</param>
    /// <param name="order">The stable panel and generated Panel-menu ordering value.</param>
    /// <param name="defaultOpen">Whether a newly discovered panel is visible before retained state is available.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> or <paramref name="title"/> is empty.</exception>
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
