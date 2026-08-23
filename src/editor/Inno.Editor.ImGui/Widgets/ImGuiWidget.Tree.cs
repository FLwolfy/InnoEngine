using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.ImGuiWidget;

/// <summary>
/// Provides reusable editor controls and rendering helpers built on the native ImGui API.
/// </summary>
public static partial class ImGuiWidget
{
    private static readonly Dictionary<int, TreeWidgetWindowState> s_treeStatesByWindow = [];
    private static bool s_hasNextTreeNodeOpen;
    private static bool s_nextTreeNodeOpen;

    private static List<TreeWidgetNodeState> s_treeNodeStack => GetTreeWindowState().treeNodeStack;
    private static List<string> s_lastNodeIdsByDepth => GetTreeWindowState().lastNodeIdsByDepth;
    private static List<TreeWidgetLineSegment> s_lineSegments => GetTreeWindowState().lineSegments;
    private static List<TreeWidgetHighlightRect> s_highlightRects => GetTreeWindowState().highlightRects;
    private static List<TreeWidgetHighlightRect> s_previousHighlightRects => GetTreeWindowState().previousHighlightRects;
    private static Dictionary<string, bool> s_hasNextSiblingById => GetTreeWindowState().hasNextSiblingById;
    private static Dictionary<string, bool> s_openStatesById => GetTreeWindowState().openStatesById;

    private static int s_lastFrame
    {
        get => GetTreeWindowState().lastFrame;
        set => GetTreeWindowState().lastFrame = value;
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

        int depth = Math.Max(0, ImGuiP.GetCurrentWindow().DC.TreeDepth);
        PruneTreeNodeStack(depth);
        bool hasNextSibling = TrackSiblingState(id, depth);
        DrawTreeGuideLines(nodeCursor, hasNextSibling, !isLeaf);
        PushTransparentTreeNodeHeaderColors();
        bool isOpen = NativeImGui.TreeNodeEx($"##{id}", flags);
        NativeImGui.PopStyleColor(3);
        float nativeRowMaxY = NativeImGui.GetItemRectMax().Y;
        if (!isLeaf && NativeImGui.IsItemToggledOpen())
            s_openStatesById[id] = isOpen;

        TreeWidgetHighlightRect contentRect = DrawTreeNodeContentContainer(
            id,
            nodeCursor,
            nativeRowMaxY,
            onDraw,
            options.drawViewportOverlay,
            out Vector2 interactionMin);
        bool hovered = !IsPopupBlockingInteraction() &&
                       NativeImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenOverlappedByItem);
        bool clicked = hovered && NativeImGui.IsItemClicked(ImGuiMouseButton.Left);
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
            s_treeNodeStack.Add(new TreeWidgetNodeState
            {
                id = id,
                cursor = nodeCursor,
                rowMaxY = contentRect.max.Y,
                hasNextSibling = hasNextSibling
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

    /// <summary>
    /// Overrides the retained expansion state of the next submitted non-leaf tree row.
    /// </summary>
    /// <param name="open">Whether the next row should be expanded.</param>
    public static void SetNextTreeNodeOpen(bool open)
    {
        s_hasNextTreeNodeOpen = true;
        s_nextTreeNodeOpen = open;
    }

    private static TreeWidgetWindowState GetTreeWindowState()
    {
        int windowKey = NativeImGui.GetWindowDrawList().GetHashCode();
        if (!s_treeStatesByWindow.TryGetValue(windowKey, out TreeWidgetWindowState? state))
        {
            state = new TreeWidgetWindowState();
            s_treeStatesByWindow[windowKey] = state;
        }

        return state;
    }

    private static TreeWidgetHighlightRect DrawTreeNodeContentContainer(
        string id,
        Vector2 nodeCursor,
        float nativeRowMaxY,
        Action onDraw,
        Action? drawViewportOverlay,
        out Vector2 interactionMin)
    {
        ImGuiWindowPtr window = ImGuiP.GetCurrentWindow();
        Vector2 windowPos = NativeImGui.GetWindowPos();
        float contentX = nodeCursor.X + NativeImGui.GetTreeNodeToLabelSpacing();
        float contentRightX = window.WorkRect.Max.X;
        float contentBottomY = window.InnerRect.Max.Y;
        float contentOffsetX = contentX - windowPos.X + window.Scroll.X -
                               window.DC.GroupOffset.X -
                               window.DC.ColumnsOffset.X;

        NativeImGui.SameLine(contentOffsetX, 0f);
        NativeImGui.BeginGroup();
        NativeImGui.PushClipRect(new Vector2(contentX, nodeCursor.Y), new Vector2(contentRightX, contentBottomY), true);
        onDraw();
        NativeImGui.PopClipRect();
        NativeImGui.EndGroup();

        Vector2 contentMin = NativeImGui.GetItemRectMin();
        Vector2 contentMax = NativeImGui.GetItemRectMax();
        TreeWidgetHighlightRect rect = new()
        {
            min = new Vector2(windowPos.X, MathF.Min(nodeCursor.Y, contentMin.Y)),
            max = new Vector2(contentRightX, MathF.Max(nativeRowMaxY, contentMax.Y))
        };

        Vector2 hitMin = new(contentX, rect.min.Y);
        Vector2 hitMax = rect.max;
        interactionMin = hitMin;
        DrawTreeNodeViewportOverlay(window, drawViewportOverlay);
        float contentBoundaryX = window.DC.CursorMaxPos.X;
        float idealBoundaryX = window.DC.IdealMaxPos.X;
        NativeImGui.SetCursorScreenPos(hitMin);
        NativeImGui.SetNextItemAllowOverlap();
        _ = NativeImGui.InvisibleButton($"##tree_content_hit_{id}", hitMax - hitMin);
        window.DC.CursorMaxPos.X = contentBoundaryX;
        window.DC.IdealMaxPos.X = idealBoundaryX;
        return rect;
    }

    private static void DrawTreeNodeViewportOverlay(
        ImGuiWindowPtr window,
        Action? drawViewportOverlay)
    {
        if (drawViewportOverlay is null)
            return;

        float contentBoundaryX = window.DC.CursorMaxPos.X;
        float idealBoundaryX = window.DC.IdealMaxPos.X;
        drawViewportOverlay();
        window.DC.CursorMaxPos.X = contentBoundaryX;
        window.DC.IdealMaxPos.X = idealBoundaryX;
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

        TreeWidgetWindowState state = GetTreeWindowState();
        Vector2 windowPosition = NativeImGui.GetWindowPos();
        Vector2 windowSize = NativeImGui.GetWindowSize();
        Vector2 scroll = new(NativeImGui.GetScrollX(), NativeImGui.GetScrollY());
        bool canReusePreviousGeometry = state.lastFrame >= 0 &&
                                        NearlyEqual(state.zoom, style.zoom) &&
                                        NearlyEqual(state.windowPosition, windowPosition) &&
                                        NearlyEqual(state.windowSize, windowSize);
        float verticalScrollTranslation = state.scroll.Y - scroll.Y;

        state.treeNodeStack.Clear();
        state.lastNodeIdsByDepth.Clear();
        state.lineSegments.Clear();
        state.previousHighlightRects.Clear();
        if (canReusePreviousGeometry)
            CopyTranslatedTreeHighlights(state, verticalScrollTranslation);
        state.highlightRects.Clear();
        state.zoom = style.zoom;
        state.windowPosition = windowPosition;
        state.windowSize = windowSize;
        state.scroll = scroll;
        DrawPreviousTreeHighlightRects();
        s_lastFrame = frame;
    }

    private static void CopyTranslatedTreeHighlights(
        TreeWidgetWindowState state,
        float verticalTranslation)
    {
        Vector2 translation = new(0f, verticalTranslation);
        for (int i = 0; i < state.highlightRects.Count; i++)
        {
            TreeWidgetHighlightRect rect = state.highlightRects[i];
            rect.min += translation;
            rect.max += translation;
            state.previousHighlightRects.Add(rect);
        }
    }

    private static void DrawPreviousTreeHighlightRects()
    {
        if (s_previousHighlightRects.Count == 0)
            return;

        bool isDragging = ImGuiP.IsDragDropActive();
        bool popupBlocksInteraction = IsPopupBlockingInteraction();
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        for (int i = 0; i < s_previousHighlightRects.Count; i++)
        {
            TreeWidgetHighlightRect rect = s_previousHighlightRects[i];
            if (isDragging && rect.isInteractionHighlight ||
                popupBlocksInteraction && rect.isHoverHighlight)
                continue;
            drawList.AddRectFilled(rect.min, rect.max, rect.color);
        }
    }

    private static void PruneTreeNodeStack(int nativeDepth)
    {
        while (s_treeNodeStack.Count > nativeDepth)
            s_treeNodeStack.RemoveAt(s_treeNodeStack.Count - 1);
    }

    private static bool NearlyEqual(float left, float right)
        => MathF.Abs(left - right) <= 0.01f;

    private static bool NearlyEqual(Vector2 left, Vector2 right)
        => NearlyEqual(left.X, right.X) && NearlyEqual(left.Y, right.Y);

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
        float rowMaxY = nodeCursor.Y + textLineHeight + style.ItemSpacing.Y + lineOverlap;
        float rowCenterY = nodeCursor.Y + textLineHeight * 0.5f;
        float nodeToLabel = NativeImGui.GetTreeNodeToLabelSpacing();
        float guideOffset = nodeToLabel * 0.5f - ImGuiWidget.style.treeGuideLeftOffset;
        float labelStartX = nodeCursor.X + nodeToLabel;
        float fileConnectorPadding = MathF.Max(1f, style.ItemInnerSpacing.X);
        float folderConnectorPadding = fileConnectorPadding * 2f +
                                       ImGuiWidget.style.treeFolderConnectorPadding;
        float targetX = hasDisclosureArrow
            ? nodeCursor.X + guideOffset - folderConnectorPadding
            : labelStartX - fileConnectorPadding;

        for (int i = 0; i < s_treeNodeStack.Count - 1; i++)
        {
            TreeWidgetNodeState ancestorState = s_treeNodeStack[i];
            TreeWidgetNodeState pathChildState = s_treeNodeStack[i + 1];
            if (pathChildState.hasNextSibling)
            {
                float ancestorX = ancestorState.cursor.X + guideOffset;
                float ancestorStartY = MathF.Max(nodeCursor.Y, ancestorState.rowMaxY);
                AddTreeLine(new Vector2(ancestorX, ancestorStartY), new Vector2(ancestorX, rowMaxY));
            }
        }

        TreeWidgetNodeState parentState = s_treeNodeStack[^1];
        float branchX = parentState.cursor.X + guideOffset;
        float branchStartY = MathF.Max(parentState.rowMaxY, nodeCursor.Y);
        AddTreeLine(new Vector2(branchX, branchStartY), new Vector2(branchX, hasNextSibling ? rowMaxY : rowCenterY));
        if (targetX > branchX)
            AddTreeLine(new Vector2(branchX, rowCenterY), new Vector2(targetX, rowCenterY));
    }

    private static void AddTreeLine(Vector2 from, Vector2 to)
    {
        TreeWidgetLineSegment line = new()
        {
            from = SnapTreeLinePoint(from),
            to = SnapTreeLinePoint(to)
        };
        s_lineSegments.Add(line);
        NativeImGui.GetWindowDrawList().AddLine(
            line.from,
            line.to,
            NativeImGui.ColorConvertFloat4ToU32(EditorPalette.treeGuide),
            style.borderSize);
    }

    private static Vector2 SnapTreeLinePoint(Vector2 point)
    {
        return new Vector2(MathF.Floor(point.X) + 0.5f, MathF.Floor(point.Y) + 0.5f);
    }

    private static void AddTreeHighlightRect(TreeWidgetHighlightRect rect, bool selected)
    {
        rect.color = NativeImGui.GetColorU32(selected ? ImGuiCol.Header : ImGuiCol.HeaderHovered);
        rect.isInteractionHighlight = true;
        rect.isHoverHighlight = !selected;
        s_highlightRects.Add(rect);
    }

    private static void AddTreeBackgroundRect(TreeWidgetHighlightRect rect, Vector4 color)
    {
        rect.color = NativeImGui.ColorConvertFloat4ToU32(color);
        rect.isInteractionHighlight = false;
        rect.isHoverHighlight = false;
        s_highlightRects.Add(rect);
    }

}

/// <summary>Configures an interactive tree row.</summary>
public readonly struct TreeNodeOptions
{
    /// <summary>Gets whether the row is selected.</summary>
    public bool selected { get; init; }

    /// <summary>Gets whether the row has no expandable children.</summary>
    public bool isLeaf { get; init; }

    /// <summary>Gets whether a custom background is drawn behind an unselected row.</summary>
    public bool showBackground { get; init; }

    /// <summary>Gets the custom background color used when <see cref="showBackground"/> is enabled.</summary>
    public Vector4 backgroundColor { get; init; }

    /// <summary>Gets whether the row keeps its configured background while hovered.</summary>
    public bool suppressHoverHighlight { get; init; }

    /// <summary>
    /// Gets the callback that draws controls fixed to the current viewport without extending the
    /// tree's horizontal content boundary.
    /// </summary>
    public Action? drawViewportOverlay { get; init; }
}

/// <summary>Describes interaction state produced by a tree row.</summary>
public readonly struct TreeNodeResult
{
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

    /// <summary>Gets whether child content should be rendered.</summary>
    public bool isOpen { get; }

    /// <summary>Gets whether the content row was clicked.</summary>
    public bool isClicked { get; }

    /// <summary>Gets whether the content row was double-clicked.</summary>
    public bool isDoubleClicked { get; }

    /// <summary>Gets whether the full row is hovered.</summary>
    public bool isHovered { get; }

    /// <summary>Gets the row minimum screen coordinate.</summary>
    public Vector2 min { get; }

    /// <summary>Gets the row maximum screen coordinate.</summary>
    public Vector2 max { get; }

    /// <summary>Gets the minimum screen coordinate of the row's interactive content, excluding tree indentation.</summary>
    public Vector2 contentMin { get; }
}

internal sealed class TreeWidgetWindowState
{
    internal readonly List<TreeWidgetNodeState> treeNodeStack = [];
    internal readonly List<string> lastNodeIdsByDepth = [];
    internal readonly List<TreeWidgetLineSegment> lineSegments = [];
    internal readonly List<TreeWidgetHighlightRect> highlightRects = [];
    internal readonly List<TreeWidgetHighlightRect> previousHighlightRects = [];
    internal readonly Dictionary<string, bool> hasNextSiblingById = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, bool> openStatesById = new(StringComparer.Ordinal);
    internal int lastFrame = -1;
    internal float zoom = float.NaN;
    internal Vector2 windowPosition;
    internal Vector2 windowSize;
    internal Vector2 scroll;
}

internal struct TreeWidgetNodeState
{
    internal string id;
    internal Vector2 cursor;
    internal float rowMaxY;
    internal bool hasNextSibling;
}

internal struct TreeWidgetLineSegment
{
    internal Vector2 from;
    internal Vector2 to;
}

internal struct TreeWidgetHighlightRect
{
    internal Vector2 min;
    internal Vector2 max;
    internal uint color;
    internal bool isInteractionHighlight;
    internal bool isHoverHighlight;
}
