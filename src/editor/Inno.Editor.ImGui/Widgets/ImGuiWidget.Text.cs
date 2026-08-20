using System.Numerics;

using Inno.Editor.ImGui;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.Widgets;

/// <summary>
/// Provides reusable editor controls and rendering helpers built on the native ImGui API.
/// </summary>
public static partial class ImGuiWidget
{
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
