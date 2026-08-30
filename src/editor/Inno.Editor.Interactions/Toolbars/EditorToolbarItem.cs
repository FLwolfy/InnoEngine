namespace Inno.Editor.Interactions;

/// <summary>Describes one resolved toolbar command.</summary>
public sealed class EditorToolbarItem
{
    internal EditorToolbarItem(
        string actionId,
        EditorToolbarIcon icon,
        string tooltip,
        int order,
        EditorActionState status)
    {
        this.actionId = actionId;
        this.icon = icon;
        this.tooltip = tooltip;
        this.order = order;
        this.status = status;
    }

    /// <summary>Gets the stable action dispatched when this item is pressed.</summary>
    public string actionId { get; }

    /// <summary>Gets the resolved presentation-independent symbol.</summary>
    public EditorToolbarIcon icon { get; }

    /// <summary>Gets the resolved contextual tooltip.</summary>
    public string tooltip { get; }

    /// <summary>Gets the stable ordering value.</summary>
    public int order { get; }

    /// <summary>Gets the current action availability and checked state.</summary>
    public EditorActionState status { get; }
}
