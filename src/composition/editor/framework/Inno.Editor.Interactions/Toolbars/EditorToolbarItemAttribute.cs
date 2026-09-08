using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Places an editor action on a compact toolbar surface.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EditorToolbarItemAttribute : Attribute
{
    /// <summary>
    /// Creates a toolbar placement for the annotated targetless action.
    /// </summary>
    /// <param name="area">
    /// The exact interaction area whose toolbar receives the action.
    /// </param>
    /// <param name="icon">
    /// The symbol shown while the action is not checked.
    /// </param>
    /// <param name="tooltip">
    /// The fallback tooltip shown when the action has no contextual display name.
    /// </param>
    /// <param name="order">
    /// The stable ordering value within the toolbar.
    /// </param>
    /// <param name="activeIcon">
    /// The symbol shown while the action is checked, or <see cref="EditorToolbarIcon.None"/> to retain
    /// <paramref name="icon"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="area"/> or <paramref name="tooltip"/> is empty, or when
    /// <paramref name="icon"/> is <see cref="EditorToolbarIcon.None"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when an icon value is not defined.
    /// </exception>
    public EditorToolbarItemAttribute(
        string area,
        EditorToolbarIcon icon,
        string tooltip,
        int order = 0,
        EditorToolbarIcon activeIcon = EditorToolbarIcon.None)
    {
        if (string.IsNullOrWhiteSpace(area))
            throw new ArgumentException("An editor toolbar area is required.", nameof(area));
        if (!Enum.IsDefined(icon))
            throw new ArgumentOutOfRangeException(nameof(icon), icon, "Unknown editor toolbar icon.");
        if (icon == EditorToolbarIcon.None)
            throw new ArgumentException("A visible editor toolbar icon is required.", nameof(icon));
        if (string.IsNullOrWhiteSpace(tooltip))
            throw new ArgumentException("An editor toolbar tooltip is required.", nameof(tooltip));
        if (!Enum.IsDefined(activeIcon))
            throw new ArgumentOutOfRangeException(nameof(activeIcon), activeIcon, "Unknown active toolbar icon.");

        this.area = area;
        this.icon = icon;
        this.tooltip = tooltip;
        this.order = order;
        this.activeIcon = activeIcon;
    }

    /// <summary>
    /// Gets the exact toolbar interaction area.
    /// </summary>
    public string area { get; }

    /// <summary>
    /// Gets the symbol shown while the action is not checked.
    /// </summary>
    public EditorToolbarIcon icon { get; }

    /// <summary>
    /// Gets the optional replacement symbol shown while the action is checked.
    /// </summary>
    public EditorToolbarIcon activeIcon { get; }

    /// <summary>
    /// Gets the fallback tooltip.
    /// </summary>
    public string tooltip { get; }

    /// <summary>
    /// Gets the stable ordering value.
    /// </summary>
    public int order { get; }
}
