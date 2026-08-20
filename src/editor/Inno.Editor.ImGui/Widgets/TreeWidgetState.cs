using System;
using System.Collections.Generic;
using System.Numerics;

namespace Inno.Editor.ImGui.Widgets;

internal sealed class TreeWidgetWindowState
{
    internal readonly List<TreeWidgetNodeState> treeNodeStack = [];
    internal readonly List<string> lastNodeIdsByDepth = [];
    internal readonly List<TreeWidgetLineSegment> lineSegments = [];
    internal readonly List<TreeWidgetLineSegment> previousLineSegments = [];
    internal readonly List<TreeWidgetLineSegment> normalizedLineSegments = [];
    internal readonly List<TreeWidgetLineSegment> mergedLineSegments = [];
    internal readonly List<TreeWidgetHighlightRect> highlightRects = [];
    internal readonly List<TreeWidgetHighlightRect> previousHighlightRects = [];
    internal readonly Dictionary<string, bool> hasNextSiblingById = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, bool> openStatesById = new(StringComparer.Ordinal);
    internal int lastFrame = -1;
}

internal struct TreeWidgetNodeState
{
    internal string id;
    internal Vector2 cursor;
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
