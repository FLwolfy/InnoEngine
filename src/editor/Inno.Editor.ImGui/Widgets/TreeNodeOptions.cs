using System.Numerics;

namespace Inno.Editor.ImGui.Widgets;

/// <summary>
/// Configures an interactive tree row.
/// </summary>
public readonly struct TreeNodeOptions
{
    /// <summary>
    /// Gets whether the row is selected.
    /// </summary>
    public bool selected { get; init; }

    /// <summary>
    /// Gets whether the row has no expandable children.
    /// </summary>
    public bool isLeaf { get; init; }

    /// <summary>
    /// Gets whether a custom background is drawn behind an unselected row.
    /// </summary>
    public bool showBackground { get; init; }

    /// <summary>
    /// Gets the custom background color used when <see cref="showBackground"/> is enabled.
    /// </summary>
    public Vector4 backgroundColor { get; init; }

    /// <summary>
    /// Gets whether the row keeps its configured background while hovered.
    /// </summary>
    public bool suppressHoverHighlight { get; init; }
}
