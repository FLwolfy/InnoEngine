using System;
using System.Numerics;

using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.ImGuiWidget;

/// <summary>
/// Provides reusable editor controls and rendering helpers built on the native ImGui API.
/// </summary>
public static partial class ImGuiWidget
{
    /// <summary>
    /// Gets the visible bounds of the first glyph in a string at a requested font size.
    /// </summary>
    /// <param name="font">Font that owns the glyph.</param>
    /// <param name="fontSize">Requested baked font size.</param>
    /// <param name="text">Text whose first Unicode scalar identifies the glyph.</param>
    /// <returns>
    /// A vector containing the glyph's left, top, right, and bottom offsets relative to the
    /// text drawing origin.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="text"/> is empty.
    /// </exception>
    public static Vector4 GetGlyphVisualBounds(ImFontPtr font, float fontSize, string text)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("Glyph text cannot be empty.", nameof(text));

        uint codepoint = (uint)char.ConvertToUtf32(text, 0);
        ImFontBakedPtr baked = NativeImGui.GetFontBaked(font, fontSize);
        ImFontGlyphPtr glyph = NativeImGui.FindGlyph(baked, codepoint);
        if (!glyph.IsNull)
            return new Vector4(glyph.X0, glyph.Y0, glyph.X1, glyph.Y1);

        NativeImGui.PushFont(font, fontSize);
        Vector2 fallbackSize = NativeImGui.CalcTextSize(text);
        NativeImGui.PopFont();
        return new Vector4(0f, 0f, fallbackSize.X, fallbackSize.Y);
    }

    /// <summary>
    /// Draws one glyph so the center of its visible bounds matches a requested point.
    /// </summary>
    /// <param name="drawList">Draw list that receives the glyph.</param>
    /// <param name="font">Font that owns the glyph.</param>
    /// <param name="fontSize">Requested baked font size.</param>
    /// <param name="text">Text containing the glyph to draw.</param>
    /// <param name="center">Target center in screen coordinates.</param>
    /// <param name="color">Packed ImGui text color.</param>
    public static void AddGlyphCentered(
        ImDrawListPtr drawList,
        ImFontPtr font,
        float fontSize,
        string text,
        Vector2 center,
        uint color)
    {
        Vector4 bounds = GetGlyphVisualBounds(font, fontSize, text);
        Vector2 visibleCenterOffset = new(
            (bounds.X + bounds.Z) * 0.5f,
            (bounds.Y + bounds.W) * 0.5f);
        NativeImGui.AddText(
            drawList,
            font,
            fontSize,
            center - visibleCenterOffset,
            color,
            text);
    }

    /// <summary>
    /// Draws clickable text without a persistent or hovered background.
    /// </summary>
    /// <param name="id">Stable identifier used by ImGui to track the interaction.</param>
    /// <param name="text">Visible text or icon glyph.</param>
    /// <param name="tooltip">Optional tooltip displayed while the interaction is hovered.</param>
    /// <returns><see langword="true"/> when the text is pressed.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public static bool ClickableText(string id, string text, string? tooltip = null)
        => ClickableText(id, text, GetCompactClickableTextSize(), tooltip);

    /// <summary>
    /// Draws a clickable icon inside the same square interaction slot used by editor close controls.
    /// The icon has no resting background and changes only its glyph color while hovered or active.
    /// </summary>
    /// <param name="id">Stable identifier used by ImGui to track the interaction.</param>
    /// <param name="icon">The visible icon glyph.</param>
    /// <param name="tooltip">Optional tooltip displayed while the interaction is hovered.</param>
    /// <returns><see langword="true"/> when the icon is pressed.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="icon"/> is <see langword="null"/>.
    /// </exception>
    public static bool ClickableIcon(string id, string icon, string? tooltip = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(icon);
        Vector2 controlSize = GetCompactIconSize();
        Vector2 cursor = NativeImGui.GetCursorScreenPos();
        bool pressed = NativeImGui.InvisibleButton($"##clickable_icon_{id}", controlSize);
        bool hovered = NativeImGui.IsItemHovered();
        bool active = NativeImGui.IsItemActive();
        uint color = hovered || active
            ? NativeImGui.ColorConvertFloat4ToU32(EditorPalette.compactControlHovered)
            : NativeImGui.GetColorU32(ImGuiCol.Text);
        AddGlyphCentered(
            NativeImGui.GetWindowDrawList(),
            NativeImGui.GetFont(),
            NativeImGui.GetFontSize(),
            icon,
            cursor + controlSize * 0.5f,
            color);

        if (!string.IsNullOrWhiteSpace(tooltip) && hovered && NativeImGui.BeginTooltip())
        {
            NativeImGui.TextUnformatted(tooltip);
            NativeImGui.EndTooltip();
        }
        return pressed;
    }

    /// <summary>
    /// Draws clickable text centered inside an explicitly sized transparent interaction area.
    /// </summary>
    /// <param name="id">Stable identifier used by ImGui to track the interaction.</param>
    /// <param name="text">Visible text or icon glyph.</param>
    /// <param name="controlSize">Size of the transparent interaction area.</param>
    /// <param name="tooltip">Optional tooltip displayed while the interaction is hovered.</param>
    /// <returns><see langword="true"/> when the text is pressed.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when either component of <paramref name="controlSize"/> is not positive.
    /// </exception>
    public static bool ClickableText(
        string id,
        string text,
        Vector2 controlSize,
        string? tooltip = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(text);
        if (controlSize.X <= 0f || controlSize.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(controlSize),
                controlSize,
                "Clickable text size must be positive.");
        }

        Vector2 textSize = NativeImGui.CalcTextSize(text);
        Vector2 cursor = NativeImGui.GetCursorScreenPos();
        bool pressed = NativeImGui.InvisibleButton($"##clickable_text_{id}", controlSize);
        bool hovered = NativeImGui.IsItemHovered();
        bool active = NativeImGui.IsItemActive();
        DrawClickableTextPresentation(
            NativeImGui.GetWindowDrawList(),
            cursor,
            controlSize,
            text,
            textSize,
            hovered,
            active);

        if (!string.IsNullOrWhiteSpace(tooltip) && hovered && NativeImGui.BeginTooltip())
        {
            NativeImGui.TextUnformatted(tooltip);
            NativeImGui.EndTooltip();
        }

        return pressed;
    }

    /// <summary>
    /// Draws non-interactive text centered inside a reserved layout area.
    /// </summary>
    /// <param name="text">Visible text.</param>
    /// <param name="areaSize">Size of the layout area that contains the text.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when either component of <paramref name="areaSize"/> is not positive.
    /// </exception>
    public static void CenteredText(string text, Vector2 areaSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (areaSize.X <= 0f || areaSize.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(areaSize),
                areaSize,
                "Centered text area size must be positive.");
        }

        Vector2 cursor = NativeImGui.GetCursorScreenPos();
        Vector2 textSize = NativeImGui.CalcTextSize(text);
        NativeImGui.GetWindowDrawList().AddText(
            cursor + (areaSize - textSize) * 0.5f,
            NativeImGui.GetColorU32(ImGuiCol.Text),
            text);
        NativeImGui.Dummy(areaSize);
    }

    /// <summary>
    /// Draws unformatted text with a temporary foreground color.
    /// </summary>
    /// <param name="color">The foreground color applied only while drawing the text.</param>
    /// <param name="text">The literal text to draw without format-string interpretation.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public static void ColoredText(Vector4 color, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        NativeImGui.PushStyleColor(ImGuiCol.Text, color);
        try
        {
            NativeImGui.TextUnformatted(text);
        }
        finally
        {
            NativeImGui.PopStyleColor();
        }
    }

    /// <summary>
    /// Draws literal text wrapped to the remaining content width without using a native
    /// variadic formatting entry point.
    /// </summary>
    /// <param name="text">The literal text to draw.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public static void WrappedText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        NativeImGui.PushTextWrapPos(0f);
        try
        {
            NativeImGui.TextUnformatted(text);
        }
        finally
        {
            NativeImGui.PopTextWrapPos();
        }
    }

    /// <summary>
    /// Calculates a clickable text area from visible text and requested inner padding.
    /// </summary>
    /// <param name="text">Visible text whose dimensions determine the content size.</param>
    /// <param name="padding">Horizontal and vertical padding surrounding the text.</param>
    /// <returns>The complete transparent interaction area size.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when either component of <paramref name="padding"/> is negative.
    /// </exception>
    public static Vector2 GetClickableTextSize(string text, Vector2 padding)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (padding.X < 0f || padding.Y < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(padding),
                padding,
                "Clickable text padding cannot be negative.");
        }

        Vector2 textSize = NativeImGui.CalcTextSize(text);
        return textSize + padding * 2f;
    }

    /// <summary>
    /// Gets the compact fixed interaction size used by icon-style clickable text.
    /// </summary>
    /// <returns>A fixed-size area that follows the editor icon slot convention.</returns>
    public static Vector2 GetCompactClickableTextSize()
    {
        float iconSlotWidth = NativeImGui.GetTextLineHeight();
        return new Vector2(iconSlotWidth + style.iconLabelSpacing, NativeImGui.GetFrameHeight());
    }

    /// <summary>
    /// Gets the square icon interaction size shared by dock-header close controls and compact editor icons.
    /// </summary>
    /// <returns>A square size based on the current ImGui font size.</returns>
    public static Vector2 GetCompactIconSize()
    {
        float iconSlotSize = NativeImGui.GetFontSize();
        return new Vector2(iconSlotSize);
    }

    private static void DrawClickableTextPresentation(
        ImDrawListPtr drawList,
        Vector2 minimum,
        Vector2 controlSize,
        string text,
        Vector2 textSize,
        bool hovered,
        bool active)
    {
        uint color = hovered || active
            ? NativeImGui.ColorConvertFloat4ToU32(EditorPalette.compactControlHovered)
            : NativeImGui.GetColorU32(ImGuiCol.Text);
        drawList.AddText(minimum + (controlSize - textSize) * 0.5f, color, text);
    }

    /// <summary>
    /// Draws icon and text with the icon centered in a fixed slot.
    /// </summary>
    /// <param name="icon">Icon text.</param>
    /// <param name="text">Main text.</param>
    /// <param name="highlight">Whether to underline and emphasize the drawn icon and text.</param>
    public static void IconText(string icon, string text, bool highlight)
    {
        ImGuiFontScope fontScope = highlight
            ? ImGuiFont.PushStyle(ImGuiFontStyle.Bold | ImGuiFontStyle.Italic)
            : default;
        try
        {
            Vector2 cursor = NativeImGui.GetCursorScreenPos();
            ImGuiStylePtr style = NativeImGui.GetStyle();
            float iconSlotWidth = NativeImGui.GetTextLineHeight();
            Vector2 iconSize = NativeImGui.CalcTextSize(icon);
            Vector2 textSize = NativeImGui.CalcTextSize(text);
            Vector2 iconPos = new(cursor.X + (iconSlotWidth - iconSize.X) * 0.5f, cursor.Y);
            Vector2 textPos = new(cursor.X + iconSlotWidth + style.ItemInnerSpacing.X, cursor.Y);

            uint color = NativeImGui.GetColorU32(ImGuiCol.Text);
            ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
            drawList.AddText(iconPos, color, icon);
            drawList.AddText(textPos, color, text);

            if (highlight)
            {
                float lineY = cursor.Y + NativeImGui.GetTextLineHeight() -
                              ImGuiWidget.style.textDecorationOffset;
                drawList.AddLine(
                    new Vector2(cursor.X, lineY),
                    new Vector2(textPos.X + textSize.X, lineY),
                    color,
                    ImGuiWidget.style.borderSize);
            }

            NativeImGui.Dummy(new Vector2(
                iconSlotWidth + style.ItemInnerSpacing.X + textSize.X,
                NativeImGui.GetTextLineHeight()));
        }
        finally
        {
            fontScope.Dispose();
        }
    }

    private static void IconTextAt(Vector2 screenPos, string icon, string text, bool highlight)
    {
        float offsetFromWindowStart = screenPos.X - NativeImGui.GetWindowPos().X;
        NativeImGui.SameLine(offsetFromWindowStart, 0f);
        IconText(icon, text, highlight);
    }
}
