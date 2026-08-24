using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;

using Inno.Editor.ImGui;
using Inno.Native.ImGui;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using TreeNodeOptions = Inno.Editor.ImGui.ImGuiWidget.TreeNodeOptions;
using TreeNodeResult = Inno.Editor.ImGui.ImGuiWidget.TreeNodeResult;
using NativeImGui = Inno.Native.ImGui.ImGui;

using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class EditorStyleMetricsTests
{
    [Fact]
    public void ZoomScalesDimensionsAndFontWhilePreservingRatios()
    {
        var metrics = new EditorStyleMetrics();

        Assert.True(metrics.SetZoom(1.5f));
        Assert.Equal(1.5f, metrics.zoom, 3);
        Assert.Equal(9f, metrics.windowPadding.X, 3);
        Assert.Equal(3f, metrics.frameRounding, 3);
        Assert.Equal(1.875f, metrics.fontScale, 3);
        Assert.Equal(0.4f, metrics.propertyLabelRatio, 3);
        Assert.Equal(3f, metrics.assetGridDefaultScale, 3);
    }

    [Fact]
    public void ZoomCommandsClampAndResetToTheSupportedRange()
    {
        var metrics = new EditorStyleMetrics();

        Assert.True(metrics.SetZoom(100f));
        Assert.Equal(EditorStyleMetrics.C_MAX_ZOOM, metrics.zoom);
        Assert.False(metrics.ZoomIn());
        Assert.True(metrics.SetZoom(-100f));
        Assert.Equal(EditorStyleMetrics.C_MIN_ZOOM, metrics.zoom);
        Assert.False(metrics.ZoomOut());
        Assert.True(metrics.ResetZoom());
        Assert.Equal(1f, metrics.zoom);
        Assert.Throws<ArgumentOutOfRangeException>(() => metrics.SetZoom(float.NaN));
    }

    [Fact]
    public void WrappedTextRendersThroughTheSafeLiteralTextPath()
    {
        var context = NativeImGui.CreateContext();
        try
        {
            Inno.Native.ImGui.ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= Inno.Native.ImGui.ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;

            NativeImGui.NewFrame();
            _ = NativeImGui.Begin("Wrapped Text Test");
            EditorWidget.WrappedText("Literal %s text remains safe and wraps.");
            Inno.Native.ImGui.ImDrawListPtr windowDrawList = NativeImGui.GetWindowDrawList();
            Inno.Native.ImGui.ImDrawListPtr foregroundDrawList = NativeImGui.GetForegroundDrawList();
            int windowVertexCount = windowDrawList.VtxBuffer.Size;
            int foregroundVertexCount = foregroundDrawList.VtxBuffer.Size;
            EditorWidget.DropTargetHighlight(
                new Vector2(20f, 20f),
                new Vector2(120f, 80f));
            Assert.Equal(windowVertexCount, windowDrawList.VtxBuffer.Size);
            Assert.True(foregroundDrawList.VtxBuffer.Size > foregroundVertexCount);
            NativeImGui.End();
            NativeImGui.Render();
        }
        finally
        {
            NativeImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void TreeGuideDepthRemainsBalancedAcrossSupportedZoomLevels()
    {
        var context = NativeImGui.CreateContext();
        try
        {
            Inno.Native.ImGui.ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= Inno.Native.ImGui.ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;

            foreach (float zoom in new[] { 0.75f, 1f, 1.25f, 1.5f })
            {
                _ = EditorWidget.style.SetZoom(zoom);
                EditorWidget.SetupStyle();
                DrawTreeFrame();
                AssertTreeStacksContainOnlyTheOpenRoot();
            }
        }
        finally
        {
            _ = EditorWidget.style.ResetZoom();
            EditorWidget.SetupStyle();
            NativeImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void MenuSelectorPopupIsAutoSizedAndCannotScroll()
    {
        var context = NativeImGui.CreateContext();
        try
        {
            Inno.Native.ImGui.ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;

            NativeImGui.NewFrame();
            _ = NativeImGui.Begin("Menu Selector Test");
            NativeImGui.OpenPopup("##menu_selector_popup_test");
            Assert.True(EditorWidget.BeginMenuSelector("test", "Untagged", 180f, 240f));
            ImGuiWindowFlags flags = ImGuiP.GetCurrentWindow().Flags;
            Assert.True(flags.HasFlag(ImGuiWindowFlags.AlwaysAutoResize));
            Assert.True(flags.HasFlag(ImGuiWindowFlags.NoScrollbar));
            Assert.True(flags.HasFlag(ImGuiWindowFlags.NoScrollWithMouse));
            Assert.True(flags.HasFlag(ImGuiWindowFlags.NoSavedSettings));
            Assert.True(NativeImGui.GetWindowSize().X >= 240f);
            string newTag = string.Empty;
            NativeImGui.SetNextItemWidth(160f);
            _ = NativeImGui.InputTextWithHint(
                "##new_tag",
                "Add tag...",
                ref newTag,
                (nuint)128);
            float inputCenterY = (NativeImGui.GetItemRectMin().Y + NativeImGui.GetItemRectMax().Y) * 0.5f;
            NativeImGui.SameLine();
            _ = EditorWidget.ClickableText(
                "add_tag",
                "+",
                new Vector2(EditorWidget.GetCompactIconSize().X, NativeImGui.GetFrameHeight()));
            float actionCenterY = (NativeImGui.GetItemRectMin().Y + NativeImGui.GetItemRectMax().Y) * 0.5f;
            Assert.Equal(inputCenterY, actionCenterY, 3);
            NativeImGui.Selectable("Untagged");
            EditorWidget.EndMenuSelector();
            NativeImGui.End();
            NativeImGui.Render();
        }
        finally
        {
            NativeImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void TreeRegionScrollsHorizontallyOnlyForRealContentOverflow()
    {
        var context = NativeImGui.CreateContext();
        try
        {
            ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;

            DrawTreeOverflowFrame(assertRanges: false);
            DrawTreeOverflowFrame(assertRanges: true);
            DrawHorizontallyScrolledTreeFrame(0f, assertGeometry: false);
            DrawHorizontallyScrolledTreeFrame(48f, assertGeometry: false);
            DrawHorizontallyScrolledTreeFrame(48f, assertGeometry: true);
        }
        finally
        {
            NativeImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void TreeRegionReleasesViewportWidthAfterItShrinks()
    {
        var context = NativeImGui.CreateContext();
        try
        {
            ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;

            DrawResizableTreeFrame(420f, 0f, assertNoOverflow: false);
            DrawResizableTreeFrame(220f, 48f, assertNoOverflow: false);
            DrawResizableTreeFrame(220f, 48f, assertNoOverflow: true);
        }
        finally
        {
            NativeImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void TreeGuideSegmentsRemainContinuousAcrossCompactRows()
    {
        var context = NativeImGui.CreateContext();
        try
        {
            ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;
            EditorWidget.SetupStyle();

            DrawTreeFrame();
            DrawTreeFrame(assertContinuousGuides: true);
        }
        finally
        {
            NativeImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void TreeGuidesAreDrawnFromTheCurrentFrameWhileDragging()
    {
        var context = NativeImGui.CreateContext();
        try
        {
            ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;
            EditorWidget.SetupStyle();

            NativeImGui.NewFrame();
            NativeImGui.SetNextWindowSize(new Vector2(480f, 320f), ImGuiCond.Always);
            _ = NativeImGui.Begin("Dragging Tree Guide Test");
            NativeImGui.GetCurrentContext().DragDropActive = true;
            EditorWidget.SetNextTreeNodeOpen(true);
            TreeNodeResult root = EditorWidget.TreeNode(
                "drag_root",
                static () => NativeImGui.TextUnformatted("Assets"),
                new TreeNodeOptions());
            if (root.isOpen)
            {
                _ = EditorWidget.TreeNode(
                    "drag_child",
                    static () => NativeImGui.TextUnformatted("Scene"),
                    new TreeNodeOptions { isLeaf = true });
                NativeImGui.TreePop();
            }
            AssertCurrentDrawListContainsTreeGuideColor();
            NativeImGui.GetCurrentContext().DragDropActive = false;
            NativeImGui.End();
            NativeImGui.Render();
        }
        finally
        {
            NativeImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void TreeRowBackgroundsUseCurrentGeometryWhileTheWindowChanges()
    {
        var context = NativeImGui.CreateContext();
        try
        {
            ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;
            EditorWidget.SetupStyle();

            DrawCurrentTreeBackgroundFrame(
                new Vector2(20f, 20f),
                new Vector2(480f, 320f));
            DrawCurrentTreeBackgroundFrame(
                new Vector2(80f, 60f),
                new Vector2(300f, 220f));
        }
        finally
        {
            NativeImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void TreeRowsRetainTheirCompactContentHeight()
    {
        var context = NativeImGui.CreateContext();
        try
        {
            ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;
            EditorWidget.SetupStyle();

            NativeImGui.NewFrame();
            NativeImGui.SetNextWindowSize(new Vector2(320f, 240f), ImGuiCond.Always);
            _ = NativeImGui.Begin("Compact Tree Row Test");
            float expectedHeight = NativeImGui.GetTextLineHeight();
            TreeNodeResult row = EditorWidget.TreeNode(
                "compact_row",
                static () => NativeImGui.TextUnformatted("GameObject"),
                new TreeNodeOptions { isLeaf = true });
            Assert.Equal(expectedHeight, row.max.Y - row.min.Y, 3);
            NativeImGui.End();
            NativeImGui.Render();
        }
        finally
        {
            NativeImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void InspectorContentPreservesStandardPaddingWithoutArtificialHorizontalOverflow()
    {
        var context = NativeImGui.CreateContext();
        try
        {
            ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;
            EditorWidget.SetupStyle();

            DrawInspectorCardFrame(assertHorizontalRange: false);
            DrawInspectorCardFrame(assertHorizontalRange: true);
        }
        finally
        {
            NativeImGui.DestroyContext(context);
        }
    }

    private static void DrawTreeFrame(bool assertContinuousGuides = false)
    {
        NativeImGui.NewFrame();
        NativeImGui.SetNextWindowSize(new Vector2(480f, 320f), Inno.Native.ImGui.ImGuiCond.Always);
        _ = NativeImGui.Begin("Tree Zoom Test");
        EditorWidget.SetNextTreeNodeOpen(true);
        TreeNodeResult root = EditorWidget.TreeNode(
            "root",
            static () => NativeImGui.TextUnformatted("Assets"),
            new TreeNodeOptions());
        if (root.isOpen)
        {
            EditorWidget.SetNextTreeNodeOpen(true);
            TreeNodeResult folder = EditorWidget.TreeNode(
                "folder",
                static () => NativeImGui.TextUnformatted("Scene"),
                new TreeNodeOptions());
            if (folder.isOpen)
            {
                _ = EditorWidget.TreeNode(
                    "nested-file",
                    static () => NativeImGui.TextUnformatted("TestScene1.iscene"),
                    new TreeNodeOptions { isLeaf = true });
                NativeImGui.TreePop();
            }

            _ = EditorWidget.TreeNode(
                "sibling-folder",
                static () => NativeImGui.TextUnformatted("Settings"),
                new TreeNodeOptions { isLeaf = true });
            NativeImGui.TreePop();
        }
        if (assertContinuousGuides)
            AssertCurrentTreeGuideSegmentsAreContinuous();
        NativeImGui.End();
        NativeImGui.Render();
    }

    private static void DrawTreeOverflowFrame(bool assertRanges)
    {
        NativeImGui.NewFrame();
        NativeImGui.SetNextWindowSize(new Vector2(560f, 320f), ImGuiCond.Always);
        _ = NativeImGui.Begin("Tree Overflow Test");
        if (NativeImGui.BeginChild(
                "##short_tree",
                new Vector2(220f, 100f),
                ImGuiChildFlags.None,
                ImGuiWindowFlags.HorizontalScrollbar))
        {
            _ = EditorWidget.TreeNode(
                "short",
                static () => NativeImGui.TextUnformatted("Object"),
                new TreeNodeOptions { isLeaf = true });
            if (assertRanges)
            {
                Assert.Equal(0f, NativeImGui.GetScrollMaxX(), 3);
                Assert.False(ImGuiP.GetCurrentWindow().ScrollbarX);
                Assert.True(ImGuiP.GetCurrentWindow().Flags.HasFlag(
                    ImGuiWindowFlags.HorizontalScrollbar));
            }
        }
        NativeImGui.EndChild();

        if (NativeImGui.BeginChild(
                "##long_tree",
                new Vector2(220f, 100f),
                ImGuiChildFlags.None,
                ImGuiWindowFlags.HorizontalScrollbar))
        {
            _ = EditorWidget.TreeNode(
                "long",
                static () => NativeImGui.TextUnformatted(
                    "Object_With_A_Name_That_Is_Intentionally_Wider_Than_The_Tree_Viewport"),
                new TreeNodeOptions { isLeaf = true });
            if (assertRanges)
            {
                Assert.True(NativeImGui.GetScrollMaxX() > 0f);
                Assert.True(ImGuiP.GetCurrentWindow().ScrollbarX);
                Assert.True(ImGuiP.GetCurrentWindow().Flags.HasFlag(
                    ImGuiWindowFlags.HorizontalScrollbar));
            }
        }
        NativeImGui.EndChild();
        NativeImGui.End();
        NativeImGui.Render();
    }

    private static void DrawHorizontallyScrolledTreeFrame(
        float requestedScroll,
        bool assertGeometry)
    {
        NativeImGui.NewFrame();
        NativeImGui.SetNextWindowSize(new Vector2(360f, 220f), ImGuiCond.Always);
        _ = NativeImGui.Begin("Scrolled Tree Test");
        NativeImGui.SetNextWindowScroll(new Vector2(requestedScroll, 0f));
        if (NativeImGui.BeginChild(
                "##scrolled_tree",
                new Vector2(220f, 120f),
                ImGuiChildFlags.None,
                ImGuiWindowFlags.HorizontalScrollbar))
        {
            Vector2 contentCursor = default;
            TreeNodeResult row = EditorWidget.TreeNode(
                "scrolled",
                () =>
                {
                    contentCursor = NativeImGui.GetCursorScreenPos();
                    NativeImGui.TextUnformatted(
                        "Scene_With_A_Name_That_Is_Intentionally_Wider_Than_The_Tree_Viewport");
                },
                new TreeNodeOptions
                {
                    isLeaf = true,
                    showBackground = true,
                    backgroundColor = EditorPalette.hierarchySceneRow
                });
            if (assertGeometry)
            {
                ImGuiWindowPtr window = ImGuiP.GetCurrentWindow();
                Assert.True(NativeImGui.GetScrollX() > 0f);
                Assert.True(window.ScrollbarX);
                Assert.Equal(row.contentMin.X, contentCursor.X, 3);
                AssertCurrentTreeBackgroundSpansViewport(
                    window,
                    NativeImGui.ColorConvertFloat4ToU32(EditorPalette.hierarchySceneRow));
            }
        }
        NativeImGui.EndChild();
        NativeImGui.End();
        NativeImGui.Render();
    }

    private static void DrawResizableTreeFrame(
        float width,
        float requestedScroll,
        bool assertNoOverflow)
    {
        NativeImGui.NewFrame();
        NativeImGui.SetNextWindowSize(new Vector2(560f, 260f), ImGuiCond.Always);
        _ = NativeImGui.Begin("Resizable Tree Test");
        NativeImGui.SetNextWindowScroll(new Vector2(requestedScroll, 0f));
        if (NativeImGui.BeginChild(
                "##resizable_tree",
                new Vector2(width, 120f),
                ImGuiChildFlags.None,
                ImGuiWindowFlags.HorizontalScrollbar))
        {
            _ = EditorWidget.TreeNode(
                "resizable_row",
                static () => NativeImGui.TextUnformatted("GameObject"),
                new TreeNodeOptions
                {
                    isLeaf = true,
                    drawViewportOverlay = static () =>
                    {
                        ImGuiWindowPtr window = ImGuiP.GetCurrentWindow();
                        Vector2 overlaySize = new(
                            NativeImGui.GetFontSize(),
                            NativeImGui.GetTextLineHeight());
                        NativeImGui.SameLine();
                        Vector2 cursor = NativeImGui.GetCursorScreenPos();
                        NativeImGui.SetCursorScreenPos(new Vector2(
                            MathF.Max(cursor.X, window.WorkRect.Max.X - overlaySize.X),
                            cursor.Y));
                        NativeImGui.Dummy(overlaySize);
                    }
                });
            if (assertNoOverflow)
            {
                Assert.Equal(0f, NativeImGui.GetScrollX(), 3);
                Assert.Equal(0f, NativeImGui.GetScrollMaxX(), 3);
                Assert.False(ImGuiP.GetCurrentWindow().ScrollbarX);
            }
        }
        NativeImGui.EndChild();
        NativeImGui.End();
        NativeImGui.Render();
    }

    private static void AssertCurrentDrawListContainsTreeGuideColor()
    {
        uint guideColor = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.treeGuide);
        AssertCurrentDrawListContainsColor(
            guideColor,
            "The current drag frame did not submit any tree-guide vertices.");
    }

    private static void DrawCurrentTreeBackgroundFrame(Vector2 position, Vector2 size)
    {
        NativeImGui.NewFrame();
        NativeImGui.SetNextWindowPos(position, ImGuiCond.Always);
        NativeImGui.SetNextWindowSize(size, ImGuiCond.Always);
        _ = NativeImGui.Begin("Current Tree Background Test");
        EditorWidget.SetNextTreeNodeOpen(true);
        TreeNodeResult root = EditorWidget.TreeNode(
            "current_background_root",
            static () => NativeImGui.TextUnformatted("TestScene"),
            new TreeNodeOptions
            {
                showBackground = true,
                backgroundColor = EditorPalette.hierarchySceneRow,
                suppressHoverHighlight = true
            });
        if (root.isOpen)
        {
            _ = EditorWidget.TreeNode(
                "current_background_child",
                static () => NativeImGui.TextUnformatted("GameObject"),
                new TreeNodeOptions
                {
                    isLeaf = true,
                    selected = true
                });
            NativeImGui.TreePop();
        }

        AssertCurrentDrawListContainsColor(
            NativeImGui.ColorConvertFloat4ToU32(EditorPalette.hierarchySceneRow),
            "The current frame did not submit the Scene row background.");
        AssertCurrentDrawListContainsColor(
            NativeImGui.GetColorU32(ImGuiCol.Header),
            "The current frame did not submit the selected row highlight.");
        NativeImGui.End();
        NativeImGui.Render();
    }

    private static void AssertCurrentDrawListContainsColor(uint color, string failureMessage)
    {
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        for (int i = 0; i < drawList.VtxBuffer.Size; i++)
        {
            if (drawList.VtxBuffer[i].Col == color)
                return;
        }

        Assert.Fail(failureMessage);
    }

    private static void AssertCurrentTreeBackgroundSpansViewport(
        ImGuiWindowPtr window,
        uint color)
    {
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        float minimumX = float.MaxValue;
        float maximumX = float.MinValue;
        for (int i = 0; i < drawList.VtxBuffer.Size; i++)
        {
            ImDrawVert vertex = drawList.VtxBuffer[i];
            if (vertex.Col != color)
                continue;

            minimumX = MathF.Min(minimumX, vertex.Pos.X);
            maximumX = MathF.Max(maximumX, vertex.Pos.X);
        }

        Assert.NotEqual(float.MaxValue, minimumX);
        Assert.Equal(NativeImGui.GetWindowPos().X, minimumX, 3);
        Assert.Equal(window.WorkRect.Max.X, maximumX, 3);
    }

    private static void DrawInspectorCardFrame(bool assertHorizontalRange)
    {
        NativeImGui.NewFrame();
        NativeImGui.SetNextWindowSize(new Vector2(320f, 260f), ImGuiCond.Always);
        bool isOpen = true;
        EditorWidget.PanelWindow(
            "Inspector Width Test",
            ref isOpen,
            () => DrawInspectorCardBody(assertHorizontalRange),
            ImGuiWindowFlags.NoSavedSettings,
            useWindowPadding: false);
        NativeImGui.Render();
    }

    private static void DrawInspectorCardBody(bool assertHorizontalRange)
    {
        ImGuiWindowPtr panelWindow = ImGuiP.GetCurrentWindow();
        Vector2 panelBodyMinimum = NativeImGui.GetCursorScreenPos();
        Vector2 panelBodySize = NativeImGui.GetContentRegionAvail();
        if (assertHorizontalRange)
        {
            Assert.Equal(Vector2.Zero, panelWindow.WindowPadding);
            Assert.Equal(NativeImGui.GetWindowPos().X, panelBodyMinimum.X);
        }

        if (NativeImGui.BeginChild("##inspector_scroll", Vector2.Zero))
        {
            if (assertHorizontalRange)
            {
                Assert.Equal(panelBodyMinimum, NativeImGui.GetWindowPos());
                Assert.Equal(panelBodySize, NativeImGui.GetWindowSize());
            }

            EditorWidget.ConstrainedContent(
                "##inspector_content",
                () =>
                {
                    if (assertHorizontalRange)
                    {
                        Assert.Equal(
                            EditorWidget.style.windowPadding,
                            ImGuiP.GetCurrentWindow().WindowPadding);
                        Assert.Equal(
                            NativeImGui.GetWindowPos() + EditorWidget.style.windowPadding,
                            NativeImGui.GetCursorScreenPos());
                    }

                    EditorWidget.LabelChip("Tag", EditorPalette.inspectorTagLabel);
                    float tagMaximumX = NativeImGui.GetItemRectMax().X;
                    NativeImGui.SameLine(0f, 0f);
                    NativeImGui.Dummy(new Vector2(80f, NativeImGui.GetFrameHeight()));
                    if (assertHorizontalRange)
                        Assert.Equal(tagMaximumX, NativeImGui.GetItemRectMin().X, 3);
                    NativeImGui.SameLine(0f, EditorWidget.style.inspectorHeaderSectionSpacing);
                    EditorWidget.LabelChip("Layer", EditorPalette.inspectorLayerLabel);
                    float layerMaximumX = NativeImGui.GetItemRectMax().X;
                    NativeImGui.SameLine(0f, 0f);
                    NativeImGui.Dummy(new Vector2(80f, NativeImGui.GetFrameHeight()));
                    if (assertHorizontalRange)
                        Assert.Equal(layerMaximumX, NativeImGui.GetItemRectMin().X, 3);

                    bool open = EditorWidget.CollapsingCard(
                        "long_behavior",
                        "BehaviorWithAnIntentionallyLongTypeNameThatMustBeClipped",
                        drawLeadingControl: static () => NativeImGui.Dummy(
                            new Vector2(NativeImGui.GetFrameHeight())),
                        drawTrailingControl: static () => NativeImGui.Dummy(
                            new Vector2(48f, NativeImGui.GetFrameHeight())),
                        trailingControlWidth: 48f);
                    if (open)
                    {
                        NativeImGui.Unindent();
                        EditorWidget.CardBody(
                            "long_behavior",
                            static () => EditorWidget.PropertyRow(
                                "long_property",
                                "Property With A Very Long Display Name",
                                static () => NativeImGui.Dummy(new Vector2(
                                    MathF.Max(1f, NativeImGui.GetContentRegionAvail().X),
                                    NativeImGui.GetFrameHeight()))));
                        NativeImGui.Indent();
                        NativeImGui.TreePop();
                        NativeImGui.Dummy(Vector2.Zero);
                    }
                    if (assertHorizontalRange)
                        Assert.Equal(0f, NativeImGui.GetScrollMaxX(), 3);
                });
            if (assertHorizontalRange)
                Assert.Equal(0f, NativeImGui.GetScrollMaxX(), 3);
        }
        NativeImGui.EndChild();
    }

    private static void AssertCurrentTreeGuideSegmentsAreContinuous()
    {
        FieldInfo statesField = typeof(EditorWidget).GetField(
            "s_treeStatesByWindow",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var states = (IDictionary)statesField.GetValue(null)!;
        int windowKey = NativeImGui.GetWindowDrawList().GetHashCode();
        Assert.True(states.Contains(windowKey));
        object state = states[windowKey]!;
        FieldInfo segmentsField = state.GetType().GetField(
            "lineSegments",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var verticalSegments = new List<(Vector2 from, Vector2 to)>();
        foreach (object segment in (IEnumerable)segmentsField.GetValue(state)!)
        {
            Type segmentType = segment.GetType();
            Vector2 from = (Vector2)segmentType.GetField(
                "from",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(segment)!;
            Vector2 to = (Vector2)segmentType.GetField(
                "to",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(segment)!;
            if (MathF.Abs(to.X - from.X) <= MathF.Abs(to.Y - from.Y))
                verticalSegments.Add((from, to));
        }

        verticalSegments.Sort(static (left, right) =>
        {
            int byX = left.from.X.CompareTo(right.from.X);
            return byX != 0 ? byX : left.from.Y.CompareTo(right.from.Y);
        });
        bool foundMultiSegmentGuide = false;
        for (int start = 0; start < verticalSegments.Count;)
        {
            int end = start + 1;
            float maximumY = verticalSegments[start].to.Y;
            while (end < verticalSegments.Count &&
                   MathF.Abs(verticalSegments[end].from.X - verticalSegments[start].from.X) <= 0.01f)
            {
                foundMultiSegmentGuide = true;
                Assert.True(
                    verticalSegments[end].from.Y <= maximumY + 0.01f,
                    $"Tree guide gap detected between {maximumY} and {verticalSegments[end].from.Y}.");
                maximumY = MathF.Max(maximumY, verticalSegments[end].to.Y);
                end++;
            }
            start = end;
        }
        Assert.True(foundMultiSegmentGuide, "Expected at least one multi-row vertical tree guide.");
    }

    private static void AssertTreeStacksContainOnlyTheOpenRoot()
    {
        FieldInfo statesField = typeof(EditorWidget).GetField(
            "s_treeStatesByWindow",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var states = (IDictionary)statesField.GetValue(null)!;
        foreach (DictionaryEntry entry in states)
        {
            object state = entry.Value!;
            FieldInfo stackField = state.GetType().GetField(
                "treeNodeStack",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var stack = (ICollection)stackField.GetValue(state)!;
            Assert.True(stack.Count <= 1, $"Retained tree depth was {stack.Count}; expected only the open root.");
        }
    }
}
