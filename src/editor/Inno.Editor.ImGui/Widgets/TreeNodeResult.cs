using System.Numerics;

namespace Inno.Editor.ImGui.Widgets;

/// <summary>
/// Describes interaction state produced by a tree row.
/// </summary>
public readonly struct TreeNodeResult
{
    /// <summary>
    /// Gets whether child content should be rendered.
    /// </summary>
    public bool isOpen { get; }

    /// <summary>
    /// Gets whether the content row was clicked.
    /// </summary>
    public bool isClicked { get; }

    /// <summary>
    /// Gets whether the content row was double-clicked.
    /// </summary>
    public bool isDoubleClicked { get; }

    /// <summary>
    /// Gets whether the full row is hovered.
    /// </summary>
    public bool isHovered { get; }

    /// <summary>
    /// Gets the row minimum screen coordinate.
    /// </summary>
    public Vector2 min { get; }

    /// <summary>
    /// Gets the row maximum screen coordinate.
    /// </summary>
    public Vector2 max { get; }

    /// <summary>
    /// Gets the minimum screen coordinate of the row's interactive content, excluding tree indentation.
    /// </summary>
    public Vector2 contentMin { get; }

    internal TreeNodeResult(
        bool isOpen,
        bool isClicked,
        bool isDoubleClicked,
        bool isHovered,
        Vector2 min,
        Vector2 max,
        Vector2 contentMin)
    {
        this.isOpen = isOpen;
        this.isClicked = isClicked;
        this.isDoubleClicked = isDoubleClicked;
        this.isHovered = isHovered;
        this.min = min;
        this.max = max;
        this.contentMin = contentMin;
    }
}
