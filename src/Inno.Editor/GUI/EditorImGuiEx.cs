using Inno.Core.Math;
using Inno.ImGui;

using ImGuiNET;
using ImGuiNet = ImGuiNET.ImGui;

namespace Inno.Editor.GUI;

public static class EditorImGuiEx
{
    // UnderLine
    public static void UnderlineLastItem(float thickness = 1f, float yOffset = -1f)
    {
        var min = ImGuiNet.GetItemRectMin();
        var max = ImGuiNet.GetItemRectMax();

        float y = max.Y + yOffset;
        var col = ImGuiNet.GetColorU32(ImGuiCol.Text);

        ImGuiNet.GetWindowDrawList().AddLine(
            new Vector2(min.X, y),
            new Vector2(max.X, y),
            col,
            thickness
        );
    }
    
    // Icon
    public static void DrawIconAndText(string iconText, string text, float iconGap = 1.5f)
    {
        float iconCellWidth = ImGuiNet.GetFontSize() + iconGap;
        ImGuiNet.BeginGroup();

        // Row info
        float rowHeight = ImGuiNet.GetFrameHeight();
        Vector2 rowStart = ImGuiNet.GetCursorScreenPos();
        var currentFont = ImGuiHost.GetCurrentFont();

        // icon
        ImGuiHost.UseFont(ImGuiFontStyle.Icon, currentFont.size);
        Vector2 iconSize = ImGuiNet.CalcTextSize(iconText);
        float iconYOffset = (rowHeight - iconSize.y) * 0.5f - ImGuiNet.GetStyle().FramePadding.Y;
        ImGuiNet.SetCursorScreenPos(new Vector2(rowStart.x, rowStart.y + iconYOffset));
        ImGuiNet.TextUnformatted(iconText);

        // Text
        ImGuiHost.UseFont(currentFont);
        ImGuiNet.SetCursorScreenPos(new Vector2(rowStart.x + iconCellWidth, rowStart.y));
        ImGuiNet.TextUnformatted(text);
        
        ImGuiNet.EndGroup();
    }
    
    // Gizmos Overlay
    public static void DrawLine(Vector2 p1, Vector2 p2, Color color, float thickness = 1f)
    {
        var drawList = ImGuiNet.GetWindowDrawList();
        drawList.AddLine(new Vector2(p1.x, p1.y), new Vector2(p2.x, p2.y), color.ToUInt32ARGB(), thickness);
    }

    public static void DrawText(Vector2 pos, string text, Color color)
    {
        var drawList = ImGuiNet.GetWindowDrawList();
        drawList.AddText(new Vector2(pos.x, pos.y), color.ToUInt32ARGB(), text);
    }
}