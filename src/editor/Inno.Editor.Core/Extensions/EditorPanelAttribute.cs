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
    /// <param name="menuPath">
    /// The optional slash-delimited category path under the generated <c>Panel</c> menu.
    /// </param>
    /// <param name="separatorBefore">
    /// Whether the generated toggle is preceded by a separator within its final menu category.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> or <paramref name="title"/> is empty, or when
    /// <paramref name="menuPath"/> contains an empty segment.
    /// </exception>
    public EditorPanelAttribute(
        string id,
        string title,
        int order = 0,
        bool defaultOpen = true,
        string menuPath = "",
        bool separatorBefore = false)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("An editor panel identifier is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("An editor panel title is required.", nameof(title));
        this.id = id;
        this.title = title;
        this.order = order;
        this.defaultOpen = defaultOpen;
        this.menuPath = NormalizeMenuPath(menuPath);
        this.separatorBefore = separatorBefore;
    }

    /// <summary>Gets the stable panel identifier.</summary>
    public string id { get; }

    /// <summary>Gets the visible panel title.</summary>
    public string title { get; }

    /// <summary>Gets the stable panel ordering value.</summary>
    public int order { get; }

    /// <summary>Gets whether a newly discovered panel is open by default.</summary>
    public bool defaultOpen { get; }

    /// <summary>Gets the optional category path under the generated <c>Panel</c> menu.</summary>
    public string menuPath { get; }

    /// <summary>Gets whether the generated panel toggle begins a new visual group.</summary>
    public bool separatorBefore { get; }

    private static string NormalizeMenuPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        string[] segments = path.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = segments[i].Trim();
            if (segments[i].Length == 0)
            {
                throw new ArgumentException(
                    "An editor panel menu path cannot contain an empty segment.",
                    nameof(path));
            }
        }
        return string.Join('/', segments);
    }
}
