using System;
using System.Numerics;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.ImGuiWidget;

/// <summary>
/// Provides reusable editor controls and rendering helpers built on the native ImGui API.
/// </summary>
public static partial class ImGuiWidget
{
    /// <summary>
    /// Draws a compact icon-only button without a persistent background.
    /// </summary>
    /// <param name="id">Stable control identifier.</param>
    /// <param name="icon">Visible icon glyph.</param>
    /// <param name="tooltip">Optional hover tooltip.</param>
    /// <returns><see langword="true"/> when the button is pressed.</returns>
    public static bool IconButton(string id, string icon, string? tooltip = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(icon);

        Vector2 iconSize = NativeImGui.CalcTextSize(icon);
        Vector2 controlSize = GetIconButtonSize();
        Vector2 cursor = NativeImGui.GetCursorScreenPos();
        bool pressed = NativeImGui.InvisibleButton($"##icon_button_{id}", controlSize);
        bool hovered = NativeImGui.IsItemHovered();
        bool active = NativeImGui.IsItemActive();

        uint color = hovered || active
            ? NativeImGui.ColorConvertFloat4ToU32(EditorPalette.compactControlHovered)
            : NativeImGui.GetColorU32(ImGuiCol.Text);
        NativeImGui.GetWindowDrawList().AddText(cursor + (controlSize - iconSize) * 0.5f, color, icon);

        if (!string.IsNullOrWhiteSpace(tooltip) && hovered && NativeImGui.BeginTooltip())
        {
            NativeImGui.TextUnformatted(tooltip);
            NativeImGui.EndTooltip();
        }

        return pressed;
    }

    /// <summary>
    /// Gets the fixed control size used by <see cref="IconButton"/>.
    /// </summary>
    /// <returns>A size with the same fixed-width icon slot convention as IconText.</returns>
    public static Vector2 GetIconButtonSize()
    {
        float iconSlotWidth = NativeImGui.GetTextLineHeight();
        return new Vector2(iconSlotWidth + style.iconLabelSpacing, NativeImGui.GetFrameHeight());
    }

    /// <summary>
    /// Draws a compact checkbox whose checked fill uses the current text color.
    /// </summary>
    /// <param name="id">Stable control identifier.</param>
    /// <param name="value">Mutable checked state.</param>
    /// <param name="size">Visual square size in pixels.</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public static bool CompactCheckbox(string id, ref bool value, float size = -1f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (size < 0f)
            size = style.compactCheckboxSize;
        if (size <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Checkbox size must be positive.");
        }

        Vector2 initialCursor = NativeImGui.GetCursorScreenPos();
        float frameHeight = NativeImGui.GetFrameHeight();
        float verticalOffset = MathF.Max(0f, (frameHeight - size) * 0.5f);
        Vector2 min = initialCursor + new Vector2(0f, verticalOffset);
        bool changed = NativeImGui.InvisibleButton(
            $"##compact_checkbox_{id}",
            new Vector2(size, frameHeight));
        if (changed)
        {
            value = !value;
        }

        bool hovered = NativeImGui.IsItemHovered();
        Vector2 max = min + new Vector2(size);
        uint textColor = NativeImGui.GetColorU32(ImGuiCol.Text);
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        if (value)
        {
            drawList.AddRectFilled(min, max, textColor, 1f);
            uint checkColor = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.inspectorCardHeader);
            drawList.AddLine(
                min + new Vector2(size * 0.22f, size * 0.52f),
                min + new Vector2(size * 0.43f, size * 0.72f),
                checkColor,
                1.5f);
            drawList.AddLine(
                min + new Vector2(size * 0.43f, size * 0.72f),
                min + new Vector2(size * 0.80f, size * 0.28f),
                checkColor,
                1.5f);
        }
        else
        {
            drawList.AddRect(min, max, textColor, 1f);
        }

        if (hovered)
        {
            drawList.AddRect(
                min - Vector2.One,
                max + Vector2.One,
                NativeImGui.ColorConvertFloat4ToU32(EditorPalette.compactControlHovered),
                1f,
                1.5f);
        }

        return changed;
    }

    /// <summary>
    /// Draws a horizontally centered button with optional space above it.
    /// </summary>
    /// <param name="label">Visible label and optional ImGui identifier suffix.</param>
    /// <param name="topPadding">Additional vertical space above the button.</param>
    /// <returns><see langword="true"/> when the button is pressed.</returns>
    public static bool CenteredButton(string label, float topPadding = 0f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (topPadding < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(topPadding), topPadding, "Top padding cannot be negative.");
        }

        if (topPadding > 0f)
        {
            NativeImGui.SetCursorPosY(NativeImGui.GetCursorPosY() + topPadding);
        }

        float width = NativeImGui.CalcTextSize(label).X + NativeImGui.GetStyle().FramePadding.X * 2f;
        float offset = MathF.Max(0f, (NativeImGui.GetContentRegionAvail().X - width) * 0.5f);
        NativeImGui.SetCursorPosX(NativeImGui.GetCursorPosX() + offset);
        return NativeImGui.Button(label);
    }

}
