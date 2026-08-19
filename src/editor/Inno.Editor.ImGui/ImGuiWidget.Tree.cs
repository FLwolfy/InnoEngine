using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui;

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

public static partial class ImGuiWidget
{
    private static readonly Dictionary<nuint, TreeWindowState> s_treeStatesByWindow = [];
    private static bool s_hasNextTreeNodeOpen;
    private static bool s_nextTreeNodeOpen;

    private static List<TreeNodeState> s_treeNodeStack => GetTreeWindowState().treeNodeStack;
    private static List<string> s_lastNodeIdsByDepth => GetTreeWindowState().lastNodeIdsByDepth;
    private static List<TreeLineSegment> s_lineSegments => GetTreeWindowState().lineSegments;
    private static List<TreeLineSegment> s_previousLineSegments => GetTreeWindowState().previousLineSegments;
    private static List<TreeLineSegment> s_normalizedLineSegments => GetTreeWindowState().normalizedLineSegments;
    private static List<TreeLineSegment> s_mergedLineSegments => GetTreeWindowState().mergedLineSegments;
    private static List<TreeHighlightRect> s_highlightRects => GetTreeWindowState().highlightRects;
    private static List<TreeHighlightRect> s_previousHighlightRects => GetTreeWindowState().previousHighlightRects;
    private static Dictionary<string, bool> s_hasNextSiblingById => GetTreeWindowState().hasNextSiblingById;
    private static Dictionary<string, bool> s_openStatesById => GetTreeWindowState().openStatesById;

    private static int s_lastFrame
    {
        get => GetTreeWindowState().lastFrame;
        set => GetTreeWindowState().lastFrame = value;
    }

    private sealed class TreeWindowState
    {
        internal readonly List<TreeNodeState> treeNodeStack = [];
        internal readonly List<string> lastNodeIdsByDepth = [];
        internal readonly List<TreeLineSegment> lineSegments = [];
        internal readonly List<TreeLineSegment> previousLineSegments = [];
        internal readonly List<TreeLineSegment> normalizedLineSegments = [];
        internal readonly List<TreeLineSegment> mergedLineSegments = [];
        internal readonly List<TreeHighlightRect> highlightRects = [];
        internal readonly List<TreeHighlightRect> previousHighlightRects = [];
        internal readonly Dictionary<string, bool> hasNextSiblingById = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, bool> openStatesById = new(StringComparer.Ordinal);
        internal int lastFrame = -1;
    }

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
        public bool isInteractionHighlight;
    }

    public static bool TreeNode(
        string id,
        Action onDraw,
        bool highlight = false,
        bool openable = false)
    {
        TreeNodeResult result = TreeNode(
            id,
            onDraw,
            new TreeNodeOptions
            {
                selected = highlight,
                isLeaf = openable
            });
        return result.isOpen;
    }

    /// <summary>
    /// Draws a full-width interactive tree row.
    /// </summary>
    /// <param name="id">Stable row identifier.</param>
    /// <param name="onDraw">Content drawing callback.</param>
    /// <param name="options">Tree row options.</param>
    /// <returns>Interaction and row geometry for the submitted item.</returns>
    public static TreeNodeResult TreeNode(
        string id,
        Action onDraw,
        in TreeNodeOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(onDraw);

        bool isLeaf = options.isLeaf;
        BeginTreeFrameIfNeeded();
        bool open = !isLeaf && s_openStatesById.TryGetValue(id, out bool storedOpen) && storedOpen;
        if (!isLeaf && s_hasNextTreeNodeOpen)
        {
            open = s_nextTreeNodeOpen;
            s_openStatesById[id] = open;
        }

        s_hasNextTreeNodeOpen = false;
        NativeImGui.SetNextItemOpen(open, ImGuiCond.Always);

        Vector2 nodeCursor = NativeImGui.GetCursorScreenPos();
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.AllowOverlap;
        if (isLeaf)
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        else
            flags |= ImGuiTreeNodeFlags.OpenOnArrow;

        PruneTreeNodeStack(nodeCursor.X);
        int depth = s_treeNodeStack.Count;
        bool hasNextSibling = TrackSiblingState(id, depth);
        DrawTreeGuideLines(nodeCursor, hasNextSibling, !isLeaf);
        PushTransparentTreeNodeHeaderColors();
        bool isOpen = NativeImGui.TreeNodeEx($"##{id}", flags);
        NativeImGui.PopStyleColor(3);
        float nativeRowMaxY = NativeImGui.GetItemRectMax().Y;
        if (!isLeaf && NativeImGui.IsItemToggledOpen())
            s_openStatesById[id] = isOpen;

        TreeHighlightRect contentRect = DrawTreeNodeContentContainer(
            id,
            nodeCursor,
            nativeRowMaxY,
            onDraw,
            out Vector2 interactionMin);
        bool hovered = NativeImGui.IsMouseHoveringRect(contentRect.min, contentRect.max);
        bool clicked = NativeImGui.IsItemClicked(ImGuiMouseButton.Left);
        bool doubleClicked = hovered && NativeImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
        bool showHoverHighlight = hovered &&
                                  !options.suppressHoverHighlight &&
                                  !ImGuiP.IsDragDropActive();
        if (options.selected || showHoverHighlight)
            AddTreeHighlightRect(contentRect, options.selected);
        else if (options.showBackground)
            AddTreeBackgroundRect(contentRect, options.backgroundColor);

        if (isOpen && !isLeaf)
        {
            s_treeNodeStack.Add(new TreeNodeState
            {
                id = id,
                cursor = nodeCursor
            });
        }

        return new TreeNodeResult(
            isOpen && !isLeaf,
            clicked,
            doubleClicked,
            hovered,
            contentRect.min,
            contentRect.max,
            interactionMin);
    }

    public static void SetNextTreeNodeOpen(bool open)
    {
        s_hasNextTreeNodeOpen = true;
        s_nextTreeNodeOpen = open;
    }

    private static unsafe TreeWindowState GetTreeWindowState()
    {
        nuint windowKey = (nuint)NativeImGui.GetWindowDrawList().Handle;
        if (!s_treeStatesByWindow.TryGetValue(windowKey, out TreeWindowState? state))
        {
            state = new TreeWindowState();
            s_treeStatesByWindow[windowKey] = state;
        }

        return state;
    }

    private static TreeHighlightRect DrawTreeNodeContentContainer(
        string id,
        Vector2 nodeCursor,
        float nativeRowMaxY,
        Action onDraw,
        out Vector2 interactionMin)
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
            max = new Vector2(contentRightX, MathF.Max(nativeRowMaxY, contentMax.Y))
        };

        Vector2 hitMin = new(contentX, rect.min.Y);
        Vector2 hitMax = rect.max;
        interactionMin = hitMin;
        NativeImGui.SetCursorScreenPos(hitMin);
        NativeImGui.SetNextItemAllowOverlap();
        _ = NativeImGui.InvisibleButton($"##tree_content_hit_{id}", hitMax - hitMin);
        return rect;
    }

    private static void PushTransparentTreeNodeHeaderColors()
    {
        NativeImGui.PushStyleColor(ImGuiCol.Header, EditorPalette.transparent);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderHovered, EditorPalette.transparent);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderActive, EditorPalette.transparent);
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
        s_previousHighlightRects.Clear();
        s_previousHighlightRects.AddRange(s_highlightRects);
        s_highlightRects.Clear();
        DrawPreviousTreeHighlightRects();
        DrawPreviousTreeGuideLines();
        s_lastFrame = frame;
    }

    private static void DrawPreviousTreeGuideLines()
    {
        if (s_previousLineSegments.Count == 0)
            return;

        MergeTreeLineSegments();
        uint color = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.treeGuide);
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        for (int i = 0; i < s_mergedLineSegments.Count; i++)
        {
            TreeLineSegment line = s_mergedLineSegments[i];
            drawList.AddLine(line.from, line.to, color, style.borderSize);
        }
    }

    private static void DrawPreviousTreeHighlightRects()
    {
        if (s_previousHighlightRects.Count == 0)
            return;

        bool isDragging = ImGuiP.IsDragDropActive();
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        for (int i = 0; i < s_previousHighlightRects.Count; i++)
        {
            TreeHighlightRect rect = s_previousHighlightRects[i];
            if (isDragging && rect.isInteractionHighlight)
                continue;
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
        float lineOverlap = ImGuiWidget.style.treeGuideLineOverlap;
        float rowMinY = nodeCursor.Y - style.ItemSpacing.Y - lineOverlap;
        float rowMaxY = nodeCursor.Y + textLineHeight + style.ItemSpacing.Y + lineOverlap;
        float rowCenterY = nodeCursor.Y + textLineHeight * 0.5f;
        float nodeToLabel = NativeImGui.GetTreeNodeToLabelSpacing();
        float guideOffset = nodeToLabel * 0.5f - ImGuiWidget.style.treeGuideLeftOffset;
        float disclosureGap = MathF.Max(
            ImGuiWidget.style.treeDisclosureMinimumGap,
            nodeToLabel * 0.25f);
        float labelStartX = nodeCursor.X + nodeToLabel;
        float fileConnectorPadding = MathF.Max(1f, style.ItemInnerSpacing.X);
        float folderConnectorPadding = fileConnectorPadding * 2f +
                                       ImGuiWidget.style.treeFolderConnectorPadding;
        float targetX = hasDisclosureArrow
            ? nodeCursor.X + guideOffset - folderConnectorPadding
            : labelStartX - fileConnectorPadding;

        for (int i = 0; i < s_treeNodeStack.Count - 1; i++)
        {
            TreeNodeState ancestorState = s_treeNodeStack[i];
            if (s_hasNextSiblingById.TryGetValue(ancestorState.id, out bool ancestorHasNextSibling) && ancestorHasNextSibling)
            {
                float ancestorX = ancestorState.cursor.X + guideOffset;
                AddTreeLine(new Vector2(ancestorX, rowMinY), new Vector2(ancestorX, rowMaxY));
            }
        }

        TreeNodeState parentState = s_treeNodeStack[^1];
        float branchX = parentState.cursor.X + guideOffset;
        float branchStartY = parentState.cursor.Y + textLineHeight * 0.5f + disclosureGap;
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

    private static Vector2 SnapTreeLinePoint(Vector2 point)
    {
        return new Vector2(MathF.Floor(point.X) + 0.5f, MathF.Floor(point.Y) + 0.5f);
    }

    private static void MergeTreeLineSegments()
    {
        s_normalizedLineSegments.Clear();
        s_mergedLineSegments.Clear();
        for (int i = 0; i < s_previousLineSegments.Count; i++)
        {
            TreeLineSegment line = s_previousLineSegments[i];
            Vector2 from = SnapTreeLinePoint(line.from);
            Vector2 to = SnapTreeLinePoint(line.to);
            bool vertical = MathF.Abs(to.X - from.X) < MathF.Abs(to.Y - from.Y);
            if ((vertical && from.Y > to.Y) || (!vertical && from.X > to.X))
            {
                (from, to) = (to, from);
            }

            s_normalizedLineSegments.Add(new TreeLineSegment
            {
                from = from,
                to = to
            });
        }

        s_normalizedLineSegments.Sort(CompareTreeLineSegments);
        for (int i = 0; i < s_normalizedLineSegments.Count; i++)
        {
            TreeLineSegment current = s_normalizedLineSegments[i];
            if (s_mergedLineSegments.Count > 0 &&
                TryMergeTreeLineSegments(s_mergedLineSegments[^1], current, out TreeLineSegment merged))
            {
                s_mergedLineSegments[^1] = merged;
                continue;
            }

            s_mergedLineSegments.Add(current);
        }
    }

    private static int CompareTreeLineSegments(TreeLineSegment left, TreeLineSegment right)
    {
        bool leftVertical = IsVerticalTreeLine(left);
        bool rightVertical = IsVerticalTreeLine(right);
        int byOrientation = leftVertical.CompareTo(rightVertical);
        if (byOrientation != 0)
        {
            return byOrientation;
        }

        int byAxis = leftVertical
            ? left.from.X.CompareTo(right.from.X)
            : left.from.Y.CompareTo(right.from.Y);
        if (byAxis != 0)
        {
            return byAxis;
        }

        return leftVertical
            ? left.from.Y.CompareTo(right.from.Y)
            : left.from.X.CompareTo(right.from.X);
    }

    private static bool TryMergeTreeLineSegments(
        TreeLineSegment left,
        TreeLineSegment right,
        out TreeLineSegment merged)
    {
        const float epsilon = 0.1f;
        bool vertical = IsVerticalTreeLine(left);
        bool sameAxis = vertical == IsVerticalTreeLine(right) &&
            (vertical
                ? MathF.Abs(left.from.X - right.from.X) <= epsilon
                : MathF.Abs(left.from.Y - right.from.Y) <= epsilon);
        bool touching = vertical
            ? right.from.Y <= left.to.Y + epsilon
            : right.from.X <= left.to.X + epsilon;
        if (!sameAxis || !touching)
        {
            merged = default;
            return false;
        }

        merged = left;
        if (vertical)
        {
            merged.to.Y = MathF.Max(left.to.Y, right.to.Y);
        }
        else
        {
            merged.to.X = MathF.Max(left.to.X, right.to.X);
        }

        return true;
    }

    private static bool IsVerticalTreeLine(TreeLineSegment line)
    {
        return MathF.Abs(line.to.X - line.from.X) < MathF.Abs(line.to.Y - line.from.Y);
    }

    private static void AddTreeHighlightRect(TreeHighlightRect rect, bool selected)
    {
        rect.color = NativeImGui.GetColorU32(selected ? ImGuiCol.Header : ImGuiCol.HeaderHovered);
        rect.isInteractionHighlight = true;
        s_highlightRects.Add(rect);
    }

    private static void AddTreeBackgroundRect(TreeHighlightRect rect, Vector4 color)
    {
        rect.color = NativeImGui.ColorConvertFloat4ToU32(color);
        rect.isInteractionHighlight = false;
        s_highlightRects.Add(rect);
    }

}
