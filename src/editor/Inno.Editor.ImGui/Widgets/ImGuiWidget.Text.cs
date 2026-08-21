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
