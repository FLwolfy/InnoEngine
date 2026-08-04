using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

public static partial class ImGuiWidget
{
    private static readonly List<TreeNodeState> s_treeNodeStack = [];
    private static readonly List<string> s_lastNodeIdsByDepth = [];
    private static readonly List<TreeLineSegment> s_lineSegments = [];
    private static readonly List<TreeLineSegment> s_previousLineSegments = [];
    private static readonly List<TreeHighlightRect> s_highlightRects = [];
    private static readonly List<TreeHighlightRect> s_previousHighlightRects = [];
    private static readonly Dictionary<string, bool> s_hasNextSiblingById = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, bool> s_openStatesById = new(StringComparer.Ordinal);
    private static int s_lastFrame = -1;

    private struct TreeNodeState
    {
        public string id;
        public Vector2 cursor;
    }

    private struct TreeLineSegment
    {
        public Vector2 from;
        public Vector2 to;
    }

    private struct TreeHighlightRect
    {
        public Vector2 min;
        public Vector2 max;
        public uint color;
    }

    public static bool TreeNode(
        string id,
        Action onDraw,
        bool highlight = false,
        bool openable = false)
    {
        BeginTreeFrameIfNeeded();
        bool open = !openable && s_openStatesById.TryGetValue(id, out bool storedOpen) && storedOpen;
        NativeImGui.SetNextItemOpen(open, ImGuiCond.Always);

        Vector2 nodeCursor = NativeImGui.GetCursorScreenPos();
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.AllowOverlap;
        if (openable)
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        else
            flags |= ImGuiTreeNodeFlags.OpenOnArrow;

        PruneTreeNodeStack(nodeCursor.X);
        int depth = s_treeNodeStack.Count;
        bool hasNextSibling = TrackSiblingState(id, depth);
        DrawTreeGuideLines(nodeCursor, hasNextSibling, !openable);
        PushTransparentTreeNodeHeaderColors();
        bool isOpen = NativeImGui.TreeNodeEx($"##{id}", flags);
        NativeImGui.PopStyleColor(3);
        if (!openable && NativeImGui.IsItemToggledOpen())
            s_openStatesById[id] = isOpen;

        TreeHighlightRect contentRect = DrawTreeNodeContentContainer(id, nodeCursor, onDraw);
        bool hovered = NativeImGui.IsMouseHoveringRect(contentRect.min, contentRect.max);
        if (highlight || hovered)
            AddTreeHighlightRect(contentRect, highlight);

        if (isOpen && !openable)
        {
            s_treeNodeStack.Add(new TreeNodeState
            {
                id = id,
                cursor = nodeCursor
            });
        }

        return isOpen && !openable;
    }

    private static TreeHighlightRect DrawTreeNodeContentContainer(string id, Vector2 nodeCursor, Action onDraw)
    {
        Vector2 windowPos = NativeImGui.GetWindowPos();
        Vector2 windowSize = NativeImGui.GetWindowSize();
        float contentX = nodeCursor.X + NativeImGui.GetTreeNodeToLabelSpacing();
        float contentRightX = windowPos.X + windowSize.X;
        float contentBottomY = windowPos.Y + windowSize.Y;

        NativeImGui.SameLine(contentX - windowPos.X, 0f);
        NativeImGui.BeginGroup();
        NativeImGui.PushClipRect(new Vector2(contentX, nodeCursor.Y), new Vector2(contentRightX, contentBottomY), true);
        onDraw();
        NativeImGui.PopClipRect();
        NativeImGui.EndGroup();

        Vector2 contentMin = NativeImGui.GetItemRectMin();
        Vector2 contentMax = NativeImGui.GetItemRectMax();
        TreeHighlightRect rect = new()
        {
            min = new Vector2(windowPos.X, MathF.Min(nodeCursor.Y, contentMin.Y)),
            max = new Vector2(contentRightX, contentMax.Y)
        };

        Vector2 hitMin = new(contentX, rect.min.Y);
        Vector2 hitMax = rect.max;
        NativeImGui.SetCursorScreenPos(hitMin);
        NativeImGui.SetNextItemAllowOverlap();
        _ = NativeImGui.InvisibleButton($"##tree_content_hit_{id}", hitMax - hitMin);
        return rect;
    }

    private static void PushTransparentTreeNodeHeaderColors()
    {
        Vector4 transparent = Vector4.Zero;
        NativeImGui.PushStyleColor(ImGuiCol.Header, transparent);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderHovered, transparent);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderActive, transparent);
    }

    private static void BeginTreeFrameIfNeeded()
    {
        int frame = NativeImGui.GetFrameCount();
        if (frame == s_lastFrame)
            return;

        s_treeNodeStack.Clear();
        s_lastNodeIdsByDepth.Clear();
        s_previousLineSegments.Clear();
        s_previousLineSegments.AddRange(s_lineSegments);
        s_lineSegments.Clear();
        DrawPreviousTreeGuideLines();
        s_previousHighlightRects.Clear();
        s_previousHighlightRects.AddRange(s_highlightRects);
        s_highlightRects.Clear();
        DrawPreviousTreeHighlightRects();
        s_lastFrame = frame;
    }

    private static void DrawPreviousTreeGuideLines()
    {
        if (s_previousLineSegments.Count == 0)
            return;

        uint color = NativeImGui.GetColorU32(ImGuiCol.Border);
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        for (int i = 0; i < s_previousLineSegments.Count; i++)
        {
            TreeLineSegment line = s_previousLineSegments[i];
            drawList.AddLine(line.from, line.to, color, 1f);
        }
    }

    private static void DrawPreviousTreeHighlightRects()
    {
        if (s_previousHighlightRects.Count == 0)
            return;

        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        for (int i = 0; i < s_previousHighlightRects.Count; i++)
        {
            TreeHighlightRect rect = s_previousHighlightRects[i];
            drawList.AddRectFilled(rect.min, rect.max, rect.color);
        }
    }

    private static void PruneTreeNodeStack(float currentCursorX)
    {
        const float epsilon = 0.5f;
        while (s_treeNodeStack.Count > 0 && currentCursorX <= s_treeNodeStack[^1].cursor.X + epsilon)
            s_treeNodeStack.RemoveAt(s_treeNodeStack.Count - 1);
    }

    private static bool TrackSiblingState(string id, int depth)
    {
        bool hasNextSibling = s_hasNextSiblingById.TryGetValue(id, out bool cachedHasNextSibling) && cachedHasNextSibling;

        while (s_lastNodeIdsByDepth.Count <= depth)
            s_lastNodeIdsByDepth.Add(string.Empty);

        string previousId = s_lastNodeIdsByDepth[depth];
        if (!string.IsNullOrEmpty(previousId) && !string.Equals(previousId, id, StringComparison.Ordinal))
            s_hasNextSiblingById[previousId] = true;

        s_hasNextSiblingById[id] = false;
        s_lastNodeIdsByDepth[depth] = id;

        for (int i = depth + 1; i < s_lastNodeIdsByDepth.Count; i++)
            s_lastNodeIdsByDepth[i] = string.Empty;

        return hasNextSibling;
    }

    private static void DrawTreeGuideLines(Vector2 nodeCursor, bool hasNextSibling, bool hasDisclosureArrow)
    {
        if (s_treeNodeStack.Count == 0)
            return;

        ImGuiStylePtr style = NativeImGui.GetStyle();
        float textLineHeight = NativeImGui.GetTextLineHeight();
        float lineOverlap = 1f;
        float rowMinY = nodeCursor.Y - style.ItemSpacing.Y - lineOverlap;
        float rowMaxY = nodeCursor.Y + textLineHeight + style.ItemSpacing.Y + lineOverlap;
        float rowCenterY = nodeCursor.Y + textLineHeight * 0.5f;
        float nodeToLabel = NativeImGui.GetTreeNodeToLabelSpacing();
        float arrowTipOffset = nodeToLabel * 0.5f;
        float labelStartX = nodeCursor.X + nodeToLabel;
        float fileConnectorPadding = MathF.Max(1f, style.ItemInnerSpacing.X);
        float folderConnectorPadding = fileConnectorPadding * 2f + 2f;
        float targetX = hasDisclosureArrow
            ? nodeCursor.X + arrowTipOffset - folderConnectorPadding
            : labelStartX - fileConnectorPadding;

        for (int i = 0; i < s_treeNodeStack.Count - 1; i++)
        {
            TreeNodeState ancestorState = s_treeNodeStack[i];
            if (s_hasNextSiblingById.TryGetValue(ancestorState.id, out bool ancestorHasNextSibling) && ancestorHasNextSibling)
            {
                float ancestorX = ancestorState.cursor.X + arrowTipOffset;
                AddTreeLine(new Vector2(ancestorX, rowMinY), new Vector2(ancestorX, rowMaxY));
            }
        }

        TreeNodeState parentState = s_treeNodeStack[^1];
        float branchX = parentState.cursor.X + arrowTipOffset;
        float branchStartY = parentState.cursor.Y + textLineHeight * 0.5f;
        AddTreeLine(new Vector2(branchX, branchStartY), new Vector2(branchX, hasNextSibling ? rowMaxY : rowCenterY));
        if (targetX > branchX)
            AddTreeLine(new Vector2(branchX, rowCenterY), new Vector2(targetX, rowCenterY));
    }

    private static void AddTreeLine(Vector2 from, Vector2 to)
    {
        s_lineSegments.Add(new TreeLineSegment
        {
            from = from,
            to = to
        });
    }

    private static void AddTreeHighlightRect(TreeHighlightRect rect, bool selected)
    {
        rect.color = NativeImGui.GetColorU32(selected ? ImGuiCol.Header : ImGuiCol.HeaderHovered);
        s_highlightRects.Add(rect);
    }

}
