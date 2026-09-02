using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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
        Assert.Equal(new Vector2(4.5f, 3f), metrics.cellPadding);
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
            NativeImGui.SetNextWindowSize(new Vector2(260f, 180f), ImGuiCond.Always);
            _ = NativeImGui.Begin("Wrapped Text Test");
            float lineHeight = NativeImGui.GetTextLineHeight();
            EditorWidget.WrappedText(
                "Literal %s text remains safe and wraps. This deliberately long compilation status " +
                "must stay inside the modal content width instead of extending beyond its right edge.");
            Assert.True(NativeImGui.GetItemRectSize().Y > lineHeight);
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
    public void InlineRenameIsCenteredAndSelectsTheCompleteValueWhenFocused()
    {
        var context = NativeImGui.CreateContext();
        try
        {
            Inno.Native.ImGui.ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;
            string value = "Scene";
            bool requestFocus = true;

            NativeImGui.NewFrame();
            _ = NativeImGui.Begin("Inline Rename Test");
            Vector2 rowMin = NativeImGui.GetCursorScreenPos();
            float rowHeight = NativeImGui.GetFrameHeight() + 8f;
            _ = EditorWidget.InlineRename(
                "scene",
                ref value,
                ref requestFocus,
                rowHeight,
                width: 180f);
            Vector2 fieldMin = NativeImGui.GetItemRectMin();
            Vector2 fieldMax = NativeImGui.GetItemRectMax();
            Assert.True(fieldMin.Y >= rowMin.Y);
            Assert.True(fieldMax.Y <= rowMin.Y + rowHeight);
            Assert.True(fieldMax.Y - fieldMin.Y < rowHeight);
            Assert.Equal(rowMin.Y + rowHeight * 0.5f, (fieldMin.Y + fieldMax.Y) * 0.5f, 3);
            NativeImGui.TextUnformatted("Content after inline rename.");
            Assert.NotEqual(0u, ImGuiP.GetCurrentWindow().ID);
            NativeImGui.End();
            NativeImGui.Render();

            NativeImGui.NewFrame();
            _ = NativeImGui.Begin("Inline Rename Test");
            _ = EditorWidget.InlineRename(
                "scene",
                ref value,
                ref requestFocus,
                rowHeight,
                width: 180f);
            Vector2 focusedFieldMin = NativeImGui.GetItemRectMin();
            Vector2 focusedFieldMax = NativeImGui.GetItemRectMax();
            uint inputId = NativeImGui.GetItemID();
            ImGuiInputTextStatePtr inputState = ImGuiP.GetInputTextState(inputId);

            Assert.False(requestFocus);
            Assert.False(inputState.IsNull);
            Assert.Equal(0, ImGuiP.GetSelectionStart(inputState));
            Assert.Equal(value.Length, ImGuiP.GetSelectionEnd(inputState));
            uint navCursorColor = NativeImGui.GetColorU32(ImGuiCol.NavCursor);
            AssertDrawListDoesNotContainColor(
                NativeImGui.GetWindowDrawList(),
                navCursorColor,
                "The inline rename focus outline was submitted to the window draw list.");
            AssertColorBoundsStayCloseToItem(
                NativeImGui.GetForegroundDrawList(),
                navCursorColor,
                focusedFieldMin,
                focusedFieldMax,
                1f + EditorWidget.style.interactionOverlayThickness);
            NativeImGui.End();
            NativeImGui.Render();
        }
        finally
        {
            NativeImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void InlineRenameCentersWithinTheNativeTableRowBounds()
    {
        var context = NativeImGui.CreateContext();
        try
        {
            ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;
            string value = "Scene";
            bool requestFocus = false;

            NativeImGui.NewFrame();
            _ = NativeImGui.Begin("Inline Rename Table Test");
            NativeImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6f, 3f));
            Assert.True(NativeImGui.BeginTable("##rename_table", 1));
            float rowHeight = NativeImGui.GetTextLineHeight() +
                              NativeImGui.GetStyle().CellPadding.Y * 2f;
            NativeImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            _ = NativeImGui.TableSetColumnIndex(0);
            ImGuiTablePtr table = ImGuiP.GetCurrentTable();
            Vector2 cursor = NativeImGui.GetCursorScreenPos();
            NativeImGui.SetCursorScreenPos(new Vector2(cursor.X, table.RowPosY1));

            _ = EditorWidget.InlineRename(
                "table_scene",
                ref value,
                ref requestFocus,
                table.RowPosY2 - table.RowPosY1,
                width: 180f);
            Vector2 fieldMin = NativeImGui.GetItemRectMin();
            Vector2 fieldMax = NativeImGui.GetItemRectMax();

            Assert.True(fieldMin.Y >= table.RowPosY1);
            Assert.True(fieldMax.Y <= table.RowPosY2);
            Assert.Equal(
                (table.RowPosY1 + table.RowPosY2) * 0.5f,
                (fieldMin.Y + fieldMax.Y) * 0.5f,
                3);
            NativeImGui.EndTable();
            NativeImGui.PopStyleVar();
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
    public void MenuSelectorPopupIsBoundedAndScrollsLongContent()
    {
        var context = NativeImGui.CreateContext();
        try
        {
            Inno.Native.ImGui.ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;

            for (int frame = 0; frame < 2; frame++)
            {
                NativeImGui.NewFrame();
                _ = NativeImGui.Begin("Menu Selector Test");
                if (frame == 0)
                    NativeImGui.OpenPopup("##menu_selector_popup_test");
                Assert.True(EditorWidget.BeginMenuSelector("test", "Untagged", 180f, 240f));
                ImGuiWindowFlags flags = ImGuiP.GetCurrentWindow().Flags;
                Assert.True(flags.HasFlag(ImGuiWindowFlags.AlwaysAutoResize));
                Assert.False(flags.HasFlag(ImGuiWindowFlags.NoScrollbar));
                Assert.False(flags.HasFlag(ImGuiWindowFlags.NoScrollWithMouse));
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
                for (int index = 0; index < 64; index++)
                    NativeImGui.Selectable($"Entry {index:D2}");
                if (frame == 1)
                {
                    Assert.True(NativeImGui.GetWindowSize().Y <= io.DisplaySize.Y * 0.70f + 1f);
                    Assert.True(NativeImGui.GetScrollMaxY() > 0f);
                }
                EditorWidget.EndMenuSelector();
                NativeImGui.End();
                NativeImGui.Render();
            }
        }
        finally
        {
            NativeImGui.DestroyContext(context);
        }
    }

    [Fact]
    public void NativeComboPopupUsesTheSharedBoundedScrollingContract()
    {
        var context = NativeImGui.CreateContext();
        try
        {
            ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;

            var popupWidths = new List<float>();
            int openFrames = 0;
            for (int frame = 0; frame < 5; frame++)
            {
                NativeImGui.NewFrame();
                NativeImGui.SetNextWindowPos(new Vector2(400f, 340f), ImGuiCond.Always);
                NativeImGui.SetNextWindowSize(new Vector2(220f, 120f), ImGuiCond.Always);
                _ = NativeImGui.Begin("Bounded Combo Test");
                uint parentViewportId = NativeImGui.GetWindowViewport().ID;
                if (frame == 0)
                {
                    uint comboId = NativeImGui.GetID("##asset");
                    uint popupId = ImGuiP.ImHashStr("##ComboPopup", comboId);
                    ImGuiP.OpenPopupEx(popupId);
                }
                NativeImGui.SetNextItemWidth(180f);
                bool open = EditorWidget.BeginBoundedCombo("##asset", "project:Material");
                if (open)
                {
                    openFrames++;
                    Assert.Equal(parentViewportId, NativeImGui.GetWindowViewport().ID);
                    string search = string.Empty;
                    _ = EditorWidget.SearchInput("assets", "Search assets...", ref search);
                    for (int index = 0; index < 64; index++)
                        NativeImGui.Selectable($"project:Material{index:D2}");
                    popupWidths.Add(NativeImGui.GetWindowSize().X);
                    if (openFrames >= 2)
                    {
                        Assert.InRange(NativeImGui.GetWindowSize().X, 179f, 181f);
                        Assert.True(NativeImGui.GetWindowSize().Y <= io.DisplaySize.Y * 0.70f + 1f);
                        Assert.True(NativeImGui.GetScrollMaxY() > 0f);
                        ImGuiViewportPtr popupViewport = NativeImGui.GetWindowViewport();
                        Vector2 popupMinimum = NativeImGui.GetWindowPos();
                        Vector2 popupMaximum = popupMinimum + NativeImGui.GetWindowSize();
                        Assert.True(popupMinimum.X >= popupViewport.WorkPos.X - 1f);
                        Assert.True(popupMinimum.Y >= popupViewport.WorkPos.Y - 1f);
                        Assert.True(popupMaximum.X <= popupViewport.WorkPos.X + popupViewport.WorkSize.X + 1f);
                        Assert.True(popupMaximum.Y <= popupViewport.WorkPos.Y + popupViewport.WorkSize.Y + 1f);
                    }
                    NativeImGui.EndCombo();
                }
                NativeImGui.End();
                NativeImGui.Render();
            }
            Assert.True(openFrames >= 2);
            Assert.All(popupWidths, width => Assert.InRange(width, 179f, 181f));
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
    public void ChildGuideStartsAtTheParentCenterWithoutChangingCompactRowHeight()
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
            _ = NativeImGui.Begin("Connected Parent Guide Test");
            float expectedHeight = NativeImGui.GetTextLineHeight();
            EditorWidget.SetNextTreeNodeOpen(true);
            TreeNodeResult parent = EditorWidget.TreeNode(
                "connected_parent",
                static _ => NativeImGui.TextUnformatted("Parent"),
                new TreeNodeOptions());
            Assert.True(parent.isOpen);
            TreeNodeResult child = EditorWidget.TreeNode(
                "connected_child",
                static _ => NativeImGui.TextUnformatted("Child"),
                new TreeNodeOptions { isLeaf = true });
            NativeImGui.TreePop();

            Assert.Equal(expectedHeight, parent.max.Y - parent.min.Y, 3);
            Assert.Equal(expectedHeight, child.max.Y - child.min.Y, 3);
            AssertCurrentTreeGuideTouchesY((parent.min.Y + parent.max.Y) * 0.5f);
            NativeImGui.End();
            NativeImGui.Render();
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
                static _ => NativeImGui.TextUnformatted("Assets"),
                new TreeNodeOptions());
            if (root.isOpen)
            {
                _ = EditorWidget.TreeNode(
                    "drag_child",
                    static _ => NativeImGui.TextUnformatted("Scene"),
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
                static _ => NativeImGui.TextUnformatted("GameObject"),
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
    public void DoubleClickingTreeContentTogglesNonLeafExpansion()
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

            TreeNodeResult initial = DrawDoubleClickTreeFrame();
            Vector2 contentCenter = new(
                initial.contentMin.X + 20f,
                (initial.min.Y + initial.max.Y) * 0.5f);
            io.AddMousePosEvent(contentCenter.X, contentCenter.Y);
            io.AddMouseButtonEvent(0, true);
            _ = DrawDoubleClickTreeFrame();
            io.AddMouseButtonEvent(0, false);
            _ = DrawDoubleClickTreeFrame();
            io.AddMouseButtonEvent(0, true);
            TreeNodeResult doubleClicked = DrawDoubleClickTreeFrame();
            io.AddMouseButtonEvent(0, false);
            TreeNodeResult expanded = DrawDoubleClickTreeFrame();

            Assert.True(doubleClicked.isDoubleClicked);
            Assert.True(expanded.isOpen);
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
            static _ => NativeImGui.TextUnformatted("Assets"),
            new TreeNodeOptions());
        if (root.isOpen)
        {
            EditorWidget.SetNextTreeNodeOpen(true);
            TreeNodeResult folder = EditorWidget.TreeNode(
                "folder",
                static _ => NativeImGui.TextUnformatted("Scene"),
                new TreeNodeOptions());
            if (folder.isOpen)
            {
                _ = EditorWidget.TreeNode(
                    "nested-file",
                    static _ => NativeImGui.TextUnformatted("TestScene1.iscene"),
                    new TreeNodeOptions { isLeaf = true });
                NativeImGui.TreePop();
            }

            _ = EditorWidget.TreeNode(
                "sibling-folder",
                static _ => NativeImGui.TextUnformatted("Settings"),
                new TreeNodeOptions { isLeaf = true });
            NativeImGui.TreePop();
        }
        if (assertContinuousGuides)
            AssertCurrentDrawListContainsTreeGuideColor();
        NativeImGui.End();
        NativeImGui.Render();
    }

    private static TreeNodeResult DrawDoubleClickTreeFrame()
    {
        NativeImGui.NewFrame();
        NativeImGui.SetNextWindowPos(new Vector2(20f, 20f), ImGuiCond.Always);
        NativeImGui.SetNextWindowSize(new Vector2(320f, 240f), ImGuiCond.Always);
        _ = NativeImGui.Begin("Double Click Tree Test");
        TreeNodeResult result = EditorWidget.TreeNode(
            "double_click_parent",
            static _ => NativeImGui.TextUnformatted("Parent"),
            new TreeNodeOptions());
        if (result.isOpen)
        {
            _ = EditorWidget.TreeNode(
                "double_click_child",
                static _ => NativeImGui.TextUnformatted("Child"),
                new TreeNodeOptions { isLeaf = true });
            NativeImGui.TreePop();
        }
        NativeImGui.End();
        NativeImGui.Render();
        return result;
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
                static _ => NativeImGui.TextUnformatted("Object"),
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
                static _ => NativeImGui.TextUnformatted(
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
                _ =>
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
                static _ => NativeImGui.TextUnformatted("GameObject"),
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

    private static void AssertCurrentTreeGuideTouchesY(float expectedY)
    {
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        uint guideColor = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.treeGuide);
        for (int i = 0; i < drawList.VtxBuffer.Size; i++)
        {
            ImDrawVert vertex = drawList.VtxBuffer[i];
            if (vertex.Col == guideColor && MathF.Abs(vertex.Pos.Y - expectedY) <= 1f)
                return;
        }

        Assert.Fail("The child guide did not connect to the parent row's vertical center.");
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
            static _ => NativeImGui.TextUnformatted("TestScene"),
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
                static _ => NativeImGui.TextUnformatted("GameObject"),
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

    private static void AssertDrawListDoesNotContainColor(
        ImDrawListPtr drawList,
        uint color,
        string failureMessage)
    {
        for (int i = 0; i < drawList.VtxBuffer.Size; i++)
        {
            if (drawList.VtxBuffer[i].Col == color)
                Assert.Fail(failureMessage);
        }
    }

    private static void AssertColorBoundsStayCloseToItem(
        ImDrawListPtr drawList,
        uint color,
        Vector2 itemMin,
        Vector2 itemMax,
        float maximumExpansion)
    {
        Vector2 colorMin = new(float.MaxValue);
        Vector2 colorMax = new(float.MinValue);
        for (int i = 0; i < drawList.VtxBuffer.Size; i++)
        {
            ImDrawVert vertex = drawList.VtxBuffer[i];
            if (vertex.Col != color)
                continue;

            colorMin = Vector2.Min(colorMin, vertex.Pos);
            colorMax = Vector2.Max(colorMax, vertex.Pos);
        }

        Assert.NotEqual(float.MaxValue, colorMin.X);
        Assert.True(colorMin.X < itemMin.X);
        Assert.True(colorMin.Y < itemMin.Y);
        Assert.True(colorMax.X > itemMax.X);
        Assert.True(colorMax.Y > itemMax.Y);
        Assert.True(colorMin.X >= itemMin.X - maximumExpansion);
        Assert.True(colorMin.Y >= itemMin.Y - maximumExpansion);
        Assert.True(colorMax.X <= itemMax.X + maximumExpansion);
        Assert.True(colorMax.Y <= itemMax.Y + maximumExpansion);
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

}
