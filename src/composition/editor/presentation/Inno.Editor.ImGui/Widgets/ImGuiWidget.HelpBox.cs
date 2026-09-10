using System;
using System.Numerics;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.ImGuiWidget;

public static partial class ImGuiWidget
{
    /// <summary>Draws a wrapped contextual message with a semantic icon and a subdued status surface.</summary>
    /// <param name="text">Literal message shown within the current content width.</param>
    /// <param name="icon">Icon-font glyph identifying the message severity.</param>
    /// <param name="color">Semantic accent for the icon, outline, and leading stripe.</param>
    public static void HelpBox(string text, string icon, Vector4 color)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(icon);
        Vector2 origin = NativeImGui.GetCursorScreenPos();
        Vector2 padding = new(10f * style.zoom, 8f * style.zoom);
        float width = MathF.Max(1f, NativeImGui.GetContentRegionAvail().X);
        float iconWidth = NativeImGui.CalcTextSize(icon).X;
        float gap = 8f * style.zoom;
        float textWidth = MathF.Max(1f, width - padding.X * 2f - iconWidth - gap);
        Vector2 textSize = NativeImGui.CalcTextSize(text, false, textWidth);
        float height = MathF.Max(NativeImGui.GetTextLineHeight(), textSize.Y) + padding.Y * 2f;
        ImDrawListPtr draw = NativeImGui.GetWindowDrawList();
        Vector4 background = Vector4.Lerp(EditorPalette.windowBackground, color, 0.075f);
        background.W = 1f;
        Vector4 outline = color;
        outline.W = 0.28f;
        draw.AddRectFilled(origin, origin + new Vector2(width, height), NativeImGui.ColorConvertFloat4ToU32(background), 4f * style.zoom);
        draw.AddRect(origin, origin + new Vector2(width, height), NativeImGui.ColorConvertFloat4ToU32(outline), 4f * style.zoom);
        draw.AddRectFilled(origin, origin + new Vector2(2f * style.zoom, height), NativeImGui.ColorConvertFloat4ToU32(color));
        draw.AddText(origin + padding, NativeImGui.ColorConvertFloat4ToU32(color), icon);
        NativeImGui.SetCursorScreenPos(origin + padding + new Vector2(iconWidth + gap, 0f));
        NativeImGui.PushTextWrapPos(NativeImGui.GetCursorPosX() + textWidth);
        try { NativeImGui.TextUnformatted(text); }
        finally { NativeImGui.PopTextWrapPos(); }
        NativeImGui.SetCursorScreenPos(origin);
        NativeImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>Draws a section heading with hover-only description using the shared Inspector presentation.</summary>
    /// <param name="title">Section title.</param>
    /// <param name="description">Optional hover explanation.</param>
    public static void SectionHeader(string title, string? description = null)
    {
        NativeImGui.SeparatorText(title);
        DrawItemTooltip(description);
    }
}
