using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

public static partial class ImGuiWidget
{
    private struct TreeNodeLineState
    {
        public Vector2 Cursor;
        public Vector2 RectMin;
        public Vector2 RectMax;
    }

    private static readonly Dictionary<string, TreeNodeLineState> s_treeNodeLineStates = new(StringComparer.Ordinal);

    /// <summary>
    /// Draws a simple tree node and optionally connects it to its parent with hierarchy lines.
    /// </summary>
    /// <param name="id">Stable node id.</param>
    /// <param name="drawLine">Whether to draw a hierarchy line to the parent node.</param>
    /// <param name="onDrawContent">Content drawn at the node label position.</param>
    /// <param name="parent">Stable parent node id.</param>
    /// <returns>The open state returned by ImGui.</returns>
    public static bool TreeNode(string id, bool drawLine, Action onDrawContent, string? parent = null)
    {
        Vector2 cursor = NativeImGui.GetCursorScreenPos();
        if (drawLine)
            DrawTreeLine(cursor, parent);

        bool open = NativeImGui.TreeNodeEx($"##{id}", ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.OpenOnArrow);
        onDrawContent();

        s_treeNodeLineStates[id] = new TreeNodeLineState
        {
            Cursor = cursor,
            RectMin = NativeImGui.GetItemRectMin(),
            RectMax = NativeImGui.GetItemRectMax()
        };

        return open;
    }

    private static void DrawTreeLine(Vector2 cursor, string? parent)
    {
        if (string.IsNullOrEmpty(parent) || !s_treeNodeLineStates.TryGetValue(parent, out TreeNodeLineState parentState))
            return;

        ImGuiStylePtr style = NativeImGui.GetStyle();
        float nodeToLabel = NativeImGui.GetTreeNodeToLabelSpacing();
        float arrowTipOffset = nodeToLabel * 0.5f;
        float parentX = parentState.Cursor.X + arrowTipOffset;
        float childX = cursor.X + arrowTipOffset;
        float childCenterY = cursor.Y + NativeImGui.GetTextLineHeight() * 0.5f;
        float parentCenterY = parentState.Cursor.Y + NativeImGui.GetTextLineHeight() * 0.5f;
        uint color = NativeImGui.GetColorU32(ImGuiCol.Border);
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();

        drawList.AddLine(new Vector2(parentX, parentCenterY), new Vector2(parentX, childCenterY), color, 1f);
        drawList.AddLine(new Vector2(parentX, childCenterY), new Vector2(childX - style.ItemInnerSpacing.X, childCenterY), color, 1f);
    }
}
