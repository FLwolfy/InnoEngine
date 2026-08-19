using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

using Inno.Assets;
using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using static Inno.Editor.Panels.FileBrowserUtility;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

/// <summary>
/// Asset browser panel with a tree pane and filtered table view.
/// </summary>
public sealed class FileBrowserPanel : EditorPanel
{
    #region Constants
    private const float C_TREE_WIDTH = 263f;
    private const float C_TREE_MIN_WIDTH = 140f;
    private const float C_TREE_MAX_WIDTH = 520f;
    private const float C_BREADCRUMB_BAR_HEIGHT = 25f;
    private const float C_GRID_MIN_CELL_SIZE = 32f;
    private const float C_GRID_CELL_PADDING = 2f;
    private const float C_GRID_SCALE_DEFAULT = 3f;
    private const float C_GRID_SCALE_MIN = 1f;
    private const float C_GRID_SCALE_MAX = 10f;
    private const float C_LIST_ROW_SPACING = 2f;
    private const int C_SEARCH_BUFFER_SIZE = 256;
    internal static readonly Vector4 S_BG = EditorPalette.collectionHeader;
    internal static readonly Vector4 S_BG_ROW = EditorPalette.collectionRow;
    internal static readonly Vector4 S_BG_ROW_ALT = EditorPalette.collectionRowAlternate;
    private static readonly Vector4 S_BG_FIELD = new(0.235f, 0.22f, 0.27f, 1f);
    internal static readonly Vector4 S_BORDER = new(0.31f, 0.30f, 0.35f, 1f);
    internal static readonly Vector4 S_BORDER_SOFT = new(0.24f, 0.24f, 0.27f, 1f);
    internal static readonly Vector4 S_TEXT = new(0.86f, 0.86f, 0.86f, 1f);
    private static readonly Vector4 S_TEXT_MUTED = new(0.54f, 0.54f, 0.56f, 1f);
    internal static readonly Vector4 S_ACCENT = new(0.50f, 0.45f, 0.62f, 1f);
    #endregion

    #region State
    private readonly FileBrowserData m_data = new();
    private readonly FileBrowserNavigation m_navigation = new();
    private readonly FileBrowserDragDrop m_dragDrop = new();
    private readonly FileBrowserChangeTracker m_changeTracker = new();
    private readonly FileBrowserTree m_tree;

    private float m_treeWidth = C_TREE_WIDTH;
    private string m_filter = string.Empty;
    private ViewMode m_viewMode = ViewMode.List;
    private FileBrowserEntryTypeFilter m_entryTypeFilter = FileBrowserEntryTypeFilter.All;
    private FileBrowserEntryScopeFilter m_entryScopeFilter = FileBrowserEntryScopeFilter.CurrentOnly;
    private float m_gridScale = C_GRID_SCALE_DEFAULT;
    #endregion

    #region Types
    private enum ViewMode
    {
        List,
        Grid
    }

    #endregion

    #region Lifecycle
    /// <summary>
    /// Creates the panel.
    /// </summary>
    public FileBrowserPanel()
        : base("asset.file-browser", "File")
    {
        m_tree = new FileBrowserTree(m_data, m_navigation, m_dragDrop);
    }

    /// <inheritdoc />
    public override void OnAttach(EditorContext context)
    {
        m_changeTracker.Attach(context);
    }

    /// <inheritdoc />
    public override void OnDetach(EditorContext context)
    {
        m_changeTracker.Detach();
    }

    /// <inheritdoc />
    public override void OnRender(EditorContext context)
    {
        PushBrowserStyle();
        DrawBrowser(context);
        PopBrowserStyle();
    }
    #endregion

    #region Layout
    private void DrawBrowser(EditorContext context)
    {
        m_navigation.SyncExternalDirectoryChange(context.selection.currentDirectory);

        ImGuiStylePtr style = NativeImGui.GetStyle();
        float breadcrumbBarHeight = GetBreadcrumbBarHeight(context.selection.currentDirectory);
        Vector2 bodySize = new(0f, -(breadcrumbBarHeight + style.ItemSpacing.Y));
        if (NativeImGui.BeginChild("##FileBrowserMain", bodySize, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGuiTableFlags splitFlags =
                ImGuiTableFlags.NoPadOuterX |
                ImGuiTableFlags.SizingFixedFit |
                ImGuiTableFlags.NoSavedSettings;

            float splitterWidth = GetTreeSplitterWidth(style);
            NativeImGui.PushStyleVar(ImGuiStyleVar.CellPadding, Vector2.Zero);
            if (NativeImGui.BeginTable("##FileBrowserSplit", 3, splitFlags))
            {
                NativeImGui.TableSetupColumn("##Tree", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, m_treeWidth);
                NativeImGui.TableSetupColumn("##Splitter", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, splitterWidth);
                NativeImGui.TableSetupColumn("##Content", ImGuiTableColumnFlags.WidthStretch);

                NativeImGui.TableNextRow();
                _ = NativeImGui.TableSetColumnIndex(0);
                DrawTreePane(context);

                _ = NativeImGui.TableSetColumnIndex(1);
                DrawTreeSplitter(splitterWidth);

                _ = NativeImGui.TableSetColumnIndex(2);
                DrawContentPane(context);

                NativeImGui.EndTable();
            }

            NativeImGui.PopStyleVar();
        }

        NativeImGui.EndChild();
        DrawBreadcrumbBar(context, breadcrumbBarHeight);
    }

    private static float GetTreeSplitterWidth(ImGuiStylePtr style)
    {
        return MathF.Max(2f, style.DockingSeparatorSize);
    }

    private void DrawTreeSplitter(float width)
    {
        Vector2 size = new(width, MathF.Max(1f, NativeImGui.GetContentRegionAvail().Y));
        _ = NativeImGui.InvisibleButton("##TreeSplitterGrip", size);

        bool hovered = NativeImGui.IsItemHovered();
        bool active = NativeImGui.IsItemActive();
        if (hovered || active)
            NativeImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

        if (active)
        {
            Vector2 delta = NativeImGui.GetMouseDragDelta(ImGuiMouseButton.Left);
            if (MathF.Abs(delta.X) > 0f)
            {
                m_treeWidth = Math.Clamp(m_treeWidth + delta.X, C_TREE_MIN_WIDTH, C_TREE_MAX_WIDTH);
                NativeImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
            }
        }

        Vector2 min = NativeImGui.GetItemRectMin();
        Vector2 max = NativeImGui.GetItemRectMax();
        Vector4 color = active ? S_ACCENT : hovered ? S_BORDER : S_BORDER_SOFT;
        NativeImGui.AddRectFilled(NativeImGui.GetWindowDrawList(), min, max, NativeImGui.ColorConvertFloat4ToU32(color));
    }

    private void DrawTreePane(EditorContext context)
    {
        NativeImGui.PushStyleColor(ImGuiCol.ChildBg, S_BG);
        ImGuiStylePtr style = NativeImGui.GetStyle();
        Vector2 treePaneSize = new(-style.WindowPadding.X, 0f);
        if (NativeImGui.BeginChild("##TreePane", treePaneSize, ImGuiChildFlags.None))
        {
            m_tree.PrepareOpenRequests(context);
            m_tree.DrawEntry(context, string.Empty, "Assets", true);
            m_tree.ClearOpenRequests();
        }

        NativeImGui.EndChild();
        m_dragDrop.DrawSceneAssetTarget(context);
        NativeImGui.PopStyleColor();
    }

    private void DrawContentPane(EditorContext context)
    {
        NativeImGui.PushStyleColor(ImGuiCol.ChildBg, S_BG);
        if (NativeImGui.BeginChild("##ContentPane", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            DrawToolbar(context);
            DrawEntriesRegion(
                context,
                m_data.CollectVisibleEntries(context, m_entryTypeFilter, m_entryScopeFilter, m_filter));
        }

        NativeImGui.EndChild();
        m_dragDrop.DrawSceneAssetTarget(context);
        NativeImGui.PopStyleColor();
    }
    #endregion

    #region Content
    private void DrawToolbar(EditorContext context)
    {
        DrawNavigationBar(context);
        DrawViewAndSearchBar();
    }

    private void DrawNavigationBar(EditorContext context)
    {
        string current = context.selection.currentDirectory;

        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 1f));
        bool canGoBack = m_navigation.canGoBack;
        PushButtonColors(canGoBack ? S_ACCENT : S_BORDER_SOFT);

        NativeImGui.BeginDisabled(!canGoBack);
        if (NativeImGui.SmallButton($"{ImGuiIcon.AngleLeft}##Back"))
            m_navigation.GoBack(context);

        NativeImGui.EndDisabled();
        NativeImGui.PopStyleColor(3);

        NativeImGui.SameLine(0f, 2f);
        bool canGoForward = m_navigation.canGoForward;
        PushButtonColors(canGoForward ? S_ACCENT : S_BORDER_SOFT);
        NativeImGui.BeginDisabled(!canGoForward);
        if (NativeImGui.SmallButton($"{ImGuiIcon.AngleRight}##Forward"))
            m_navigation.GoForward(context);

        NativeImGui.EndDisabled();
        NativeImGui.PopStyleColor(3);

        NativeImGui.SameLine(0f, 5f);

        NativeImGui.PushStyleColor(ImGuiCol.Text, S_TEXT);
        NativeImGui.TextUnformatted(GetDirectoryLabel(current));
        NativeImGui.PopStyleColor();

        NativeImGui.PopStyleVar();
    }

    private void DrawViewAndSearchBar()
    {
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5f, 1f));

        PushButtonColors(S_ACCENT);
        if (NativeImGui.SmallButton($"{m_viewMode}##ViewMode"))
            m_viewMode = m_viewMode == ViewMode.List ? ViewMode.Grid : ViewMode.List;

        NativeImGui.PopStyleColor(3);
        NativeImGui.SameLine(0f, 4f);

        DrawEntryFilterCombo();
        NativeImGui.SameLine(0f, 4f);

        NativeImGui.SetNextItemWidth(-1f);
        DrawSearchInput();
        NativeImGui.PopStyleVar();
    }

    private void DrawEntryFilterCombo()
    {
        if (!NativeImGui.BeginCombo("##AssetEntryFilter", "Filter", ImGuiComboFlags.WidthFitPreview))
            return;

        DrawEntryTypeFilterOption("All", FileBrowserEntryTypeFilter.All);
        DrawEntryTypeFilterOption("Folders", FileBrowserEntryTypeFilter.FoldersOnly);
        DrawEntryTypeFilterOption("Files", FileBrowserEntryTypeFilter.FilesOnly);
        NativeImGui.Separator();
        DrawEntryScopeFilterOption("Current", FileBrowserEntryScopeFilter.CurrentOnly);
        DrawEntryScopeFilterOption("Recursive", FileBrowserEntryScopeFilter.Recursive);

        NativeImGui.EndCombo();
    }

    private void DrawEntryTypeFilterOption(string label, FileBrowserEntryTypeFilter filter)
    {
        bool selected = m_entryTypeFilter == filter;
        if (NativeImGui.Checkbox(label, ref selected))
        {
            if (selected)
                m_entryTypeFilter = filter;
            else if (m_entryTypeFilter == filter)
                m_entryTypeFilter = FileBrowserEntryTypeFilter.All;
        }
    }

    private void DrawEntryScopeFilterOption(string label, FileBrowserEntryScopeFilter filter)
    {
        bool selected = m_entryScopeFilter == filter;
        if (NativeImGui.Checkbox(label, ref selected))
        {
            if (selected)
                m_entryScopeFilter = filter;
            else if (m_entryScopeFilter == filter)
                m_entryScopeFilter = FileBrowserEntryScopeFilter.CurrentOnly;
        }
    }

    private void DrawEntriesRegion(EditorContext context, IReadOnlyList<AssetFileEntry> entries)
    {
        if (m_viewMode == ViewMode.List)
        {
            DrawListRegion(context, entries);
            return;
        }

        DrawGridRegion(context, entries);
    }

    private void DrawListRegion(EditorContext context, IReadOnlyList<AssetFileEntry> entries)
    {
        if (NativeImGui.BeginChild("##EntriesScroll", Vector2.Zero, ImGuiChildFlags.None))
            DrawEntriesTable(context, entries, context.selection.currentDirectory);

        NativeImGui.EndChild();
    }

    private void DrawGridRegion(EditorContext context, IReadOnlyList<AssetFileEntry> entries)
    {
        ImGuiStylePtr style = NativeImGui.GetStyle();
        float sliderHeight = NativeImGui.GetFrameHeight() + style.WindowPadding.Y * 2f + style.ItemSpacing.Y;
        if (NativeImGui.BeginChild("##EntriesScroll", new Vector2(0f, -sliderHeight), ImGuiChildFlags.None))
            DrawGrid(context, entries);

        NativeImGui.EndChild();
        DrawGridScaleSlider();
    }

    private void DrawGridScaleSlider()
    {
        m_gridScale = Math.Clamp(m_gridScale, C_GRID_SCALE_MIN, C_GRID_SCALE_MAX);
        DrawGridScaleTopSplitter();
        Vector2 cursor = NativeImGui.GetCursorPos();
        float labelOffsetY = MathF.Max(0f, (NativeImGui.GetFrameHeight() - NativeImGui.GetTextLineHeight()) * 0.5f);
        NativeImGui.SetCursorPosY(cursor.Y + labelOffsetY);
        NativeImGui.TextUnformatted("Scale");
        NativeImGui.SameLine();
        NativeImGui.SetCursorPosY(cursor.Y);
        NativeImGui.SetNextItemWidth(-1f);
        _ = NativeImGui.SliderFloat("##GridScale", ref m_gridScale, C_GRID_SCALE_MIN, C_GRID_SCALE_MAX, "%.1f");
        m_gridScale = Math.Clamp(m_gridScale, C_GRID_SCALE_MIN, C_GRID_SCALE_MAX);
    }

    private static void DrawGridScaleTopSplitter()
    {
        ImGuiStylePtr style = NativeImGui.GetStyle();
        Vector2 cursor = NativeImGui.GetCursorScreenPos();
        float width = NativeImGui.GetContentRegionAvail().X;
        float lineY = cursor.Y + style.WindowPadding.Y;
        uint color = NativeImGui.ColorConvertFloat4ToU32(S_BORDER);
        NativeImGui.GetWindowDrawList().AddLine(new Vector2(cursor.X, lineY), new Vector2(cursor.X + width, lineY), color, 1f);
        NativeImGui.SetCursorPosY(NativeImGui.GetCursorPosY() + style.WindowPadding.Y * 2f);
    }

    private void DrawEntriesTable(EditorContext context, IReadOnlyList<AssetFileEntry> entries, string currentDirectory)
    {
        ImGuiTableFlags flags =
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.BordersInnerV |
            ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.SizingFixedFit |
            ImGuiTableFlags.NoSavedSettings;

        ImGuiStylePtr style = NativeImGui.GetStyle();
        NativeImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(style.WindowPadding.X, style.CellPadding.Y));
        if (!NativeImGui.BeginTable("##FileBrowserEntries", 3, flags, new Vector2(0f, 0f)))
        {
            NativeImGui.PopStyleVar();
            return;
        }

        NativeImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 244f);
        NativeImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 164f);
        NativeImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 1f);
        DrawHeaderRow();

        uint rowBg = NativeImGui.ColorConvertFloat4ToU32(S_BG_ROW);
        uint rowAltBg = NativeImGui.ColorConvertFloat4ToU32(S_BG_ROW_ALT);
        for (int i = 0; i < entries.Count; i++)
        {
            AssetFileEntry entry = entries[i];
            if (i > 0)
                NativeImGui.TableNextRow(ImGuiTableRowFlags.None, C_LIST_ROW_SPACING);
            NativeImGui.TableNextRow();
            NativeImGui.TableSetBgColor(
                ImGuiTableBgTarget.RowBg0,
                i % 2 == 0 ? rowBg : rowAltBg);

            DrawNameCell(context, entry);
            DrawTextCell(GetTypeText(entry), S_TEXT);
            DrawTextCell(GetSourceText(entry, currentDirectory), S_TEXT);
        }

        NativeImGui.EndTable();
        NativeImGui.PopStyleVar();
    }

    private static void DrawHeaderRow()
    {
        NativeImGui.TableNextRow();
        NativeImGui.TableSetBgColor(
            ImGuiTableBgTarget.RowBg0,
            NativeImGui.ColorConvertFloat4ToU32(EditorPalette.collectionHeader));
        NativeImGui.TableSetBgColor(
            ImGuiTableBgTarget.RowBg1,
            NativeImGui.ColorConvertFloat4ToU32(EditorPalette.collectionHeader));
        _ = NativeImGui.TableSetColumnIndex(0);
        NativeImGui.TextUnformatted("Name");
        _ = NativeImGui.TableSetColumnIndex(1);
        NativeImGui.TextUnformatted("Type");
        _ = NativeImGui.TableSetColumnIndex(2);
        NativeImGui.TextUnformatted("Source");
    }

    private void DrawNameCell(EditorContext context, AssetFileEntry entry)
    {
        _ = NativeImGui.TableSetColumnIndex(0);
        string icon = entry.isDirectory ? ImGuiIcon.Folder : GetFileIcon(entry.relativePath);
        string name = entry.nameWithoutExtension;
        bool selected = string.Equals(context.selection.selectedPath, entry.relativePath, StringComparison.Ordinal);

        NativeImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderHovered, Vector4.Zero);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderActive, Vector4.Zero);
        Vector2 iconTextPos = NativeImGui.GetCursorScreenPos();
        if (NativeImGui.Selectable($"##entry_{entry.relativePath}", selected, ImGuiSelectableFlags.SpanAllColumns))
        {
            context.selection.SetSelectedPath(entry.relativePath);
            m_tree.RequestRevealPath(entry.relativePath);
        }

        bool itemHovered = NativeImGui.IsItemHovered();
        bool itemActive = NativeImGui.IsItemActive();
        if (selected || itemHovered)
        {
            Vector4 highlight = itemActive
                ? LerpColor(S_ACCENT, Vector4.One, 0.16f)
                : S_ACCENT;
            NativeImGui.TableSetBgColor(
                ImGuiTableBgTarget.RowBg1,
                NativeImGui.ColorConvertFloat4ToU32(highlight));
        }
        m_dragDrop.DrawAssetSource(entry);

        NativeImGui.SameLine(iconTextPos.X - NativeImGui.GetWindowPos().X, 0f);
        ImGuiWidget.IconText(icon, name, false);

        if (itemHovered && NativeImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            m_navigation.OpenEntry(context, entry, m_tree);

        NativeImGui.PopStyleColor(3);
    }

    private static void DrawTextCell(string text, Vector4 color)
    {
        NativeImGui.TableNextColumn();
        NativeImGui.PushStyleColor(ImGuiCol.Text, color);
        NativeImGui.TextUnformatted(text);
        NativeImGui.PopStyleColor();
    }

    private void DrawGrid(EditorContext context, IReadOnlyList<AssetFileEntry> entries)
    {
        float cellSize = GetGridCellSize();
        float available = MathF.Max(cellSize, NativeImGui.GetContentRegionAvail().X);
        int columns = Math.Max(1, (int)(available / cellSize));

        if (!NativeImGui.BeginTable("##FileBrowserGrid", columns, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.NoSavedSettings))
            return;

        for (int i = 0; i < entries.Count; i++)
        {
            NativeImGui.TableNextColumn();
            DrawGridItem(context, entries[i]);
        }

        NativeImGui.EndTable();
    }

    private void DrawGridItem(EditorContext context, AssetFileEntry entry)
    {
        float cellSize = GetGridCellSize();
        string icon = entry.isDirectory ? ImGuiIcon.Folder : GetFileIcon(entry.relativePath);
        string name = Path.GetFileName(entry.relativePath);
        bool selected = string.Equals(context.selection.selectedPath, entry.relativePath, StringComparison.Ordinal);
        Vector2 itemSize = new(cellSize - C_GRID_CELL_PADDING, cellSize - C_GRID_CELL_PADDING);

        NativeImGui.PushID(entry.relativePath);
        if (NativeImGui.InvisibleButton("##GridItem", itemSize))
        {
            context.selection.SetSelectedPath(entry.relativePath);
            m_tree.RequestRevealPath(entry.relativePath);
        }

        m_dragDrop.DrawAssetSource(entry);

        if (NativeImGui.IsItemHovered() && NativeImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            m_navigation.OpenEntry(context, entry, m_tree);

        DrawGridItemVisual(icon, name, selected, m_gridScale);
        NativeImGui.PopID();
    }

    private static unsafe void DrawGridItemVisual(string icon, string name, bool selected, float scale)
    {
        bool hovered = NativeImGui.IsItemHovered();
        bool active = NativeImGui.IsItemActive();
        Vector2 min = NativeImGui.GetItemRectMin();
        Vector2 max = NativeImGui.GetItemRectMax();
        Vector2 size = max - min;

        Vector4 bg = selected ? S_ACCENT : S_BG;
        if (active)
            bg = LerpColor(bg, Vector4.One, 0.24f);
        else if (hovered)
            bg = LerpColor(bg, Vector4.One, 0.16f);

        uint bgColor = NativeImGui.ColorConvertFloat4ToU32(bg);
        uint textColor = NativeImGui.ColorConvertFloat4ToU32(S_TEXT);
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, bgColor, 1f);

        ImFontPtr font = NativeImGui.GetFont();
        float fontSize = NativeImGui.GetFontSize();
        float iconFontSize = fontSize * scale;
        Vector2 iconSize = NativeImGui.CalcTextSize(icon) * scale;
        string[] nameLines = FitTextToLines(name, MathF.Max(1f, size.X - 10f), 2);
        float lineHeight = NativeImGui.CalcTextSize("A").Y;
        float labelHeight = lineHeight * nameLines.Length;
        float labelY = max.Y - labelHeight - 4f;
        float iconAreaCenterY = min.Y + (labelY - min.Y) * 0.5f;
        Vector2 iconPos = new(min.X + (size.X - iconSize.X) * 0.5f, iconAreaCenterY - iconSize.Y * 0.5f);

        drawList.PushClipRect(min, max, true);
        drawList.AddText(font.Handle, iconFontSize, iconPos, textColor, icon);
        for (int i = 0; i < nameLines.Length; i++)
        {
            Vector2 lineSize = NativeImGui.CalcTextSize(nameLines[i]);
            Vector2 linePosition = new(
                min.X + (size.X - lineSize.X) * 0.5f,
                labelY + lineHeight * i);
            drawList.AddText(font.Handle, fontSize, linePosition, textColor, nameLines[i]);
        }
        drawList.PopClipRect();
    }

    private float GetGridCellSize()
    {
        float fontSize = NativeImGui.GetFontSize();
        return MathF.Max(C_GRID_MIN_CELL_SIZE, fontSize * (m_gridScale + 2f) + 8f);
    }

    private void DrawSearchInput()
    {
        byte[] hintBytes = Encoding.UTF8.GetBytes($"{ImGuiIcon.MagnifyingGlass}\0");
        unsafe
        {
            fixed (byte* hint = hintBytes)
            {
                _ = NativeImGui.InputTextWithHint("##AssetSearch", hint, ref m_filter, (nuint)C_SEARCH_BUFFER_SIZE);
            }
        }
    }
    #endregion

    #region Bottom Bar
    private void DrawBreadcrumbBar(EditorContext context, float height)
    {
        IReadOnlyList<(string Label, string Path)> parts = BuildBreadcrumbParts(context.selection.currentDirectory);
        Vector2 framePadding = new(6f, 1f);
        float contentWidth = CalculateBreadcrumbContentWidth(parts, framePadding);
        NativeImGui.SetNextWindowContentSize(new Vector2(MathF.Max(contentWidth, NativeImGui.GetContentRegionAvail().X), 0f));
        NativeImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (NativeImGui.BeginChild("##BreadcrumbBar", new Vector2(0f, height), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            DrawBreadcrumbTopSeparator();
            NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, framePadding);
            PushButtonColors(S_ACCENT);
            float contentHeight = contentWidth > NativeImGui.GetWindowSize().X ? C_BREADCRUMB_BAR_HEIGHT : height;
            NativeImGui.SetCursorPosY(MathF.Max(0f, (contentHeight - NativeImGui.GetFrameHeight()) * 0.5f));

            for (int i = 0; i < parts.Count; i++)
            {
                (string label, string path) = parts[i];
                if (i > 0)
                {
                    NativeImGui.SameLine(0f, 4f);
                    NativeImGui.TextUnformatted(">");
                    NativeImGui.SameLine(0f, 4f);
                }

                if (NativeImGui.SmallButton($"{label}##crumb_{path}"))
                    m_navigation.NavigateTo(context, path);
            }

            NativeImGui.PopStyleColor(3);
            NativeImGui.PopStyleVar();
        }

        NativeImGui.EndChild();
        NativeImGui.PopStyleVar();
    }

    private static float GetBreadcrumbBarHeight(string currentDirectory)
    {
        IReadOnlyList<(string Label, string Path)> parts = BuildBreadcrumbParts(currentDirectory);
        float contentWidth = CalculateBreadcrumbContentWidth(parts, new Vector2(6f, 1f));
        return contentWidth > NativeImGui.GetContentRegionAvail().X
            ? C_BREADCRUMB_BAR_HEIGHT + NativeImGui.GetStyle().ScrollbarSize
            : C_BREADCRUMB_BAR_HEIGHT;
    }

    private static float CalculateBreadcrumbContentWidth(IReadOnlyList<(string Label, string Path)> parts, Vector2 framePadding)
    {
        if (parts.Count == 0)
            return 0f;

        float width = 0f;
        float separatorWidth = NativeImGui.CalcTextSize(">").X;
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0)
                width += separatorWidth + 8f;

            width += NativeImGui.CalcTextSize(parts[i].Label).X + framePadding.X * 2f;
        }

        return MathF.Ceiling(width);
    }

    private static void DrawBreadcrumbTopSeparator()
    {
        Vector2 min = NativeImGui.GetWindowPos();
        Vector2 size = NativeImGui.GetWindowSize();
        uint color = NativeImGui.ColorConvertFloat4ToU32(S_BORDER);
        NativeImGui.GetWindowDrawList().AddLine(min, new Vector2(min.X + size.X, min.Y), color, 1f);
    }
    #endregion

}
