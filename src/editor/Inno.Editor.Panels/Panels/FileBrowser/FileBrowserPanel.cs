using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

using Inno.Assets;
using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
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
    private const string C_ASSET_PAYLOAD = "INNO_ASSET";

    private static readonly Vector4 S_BG = EditorPalette.collectionHeader;
    private static readonly Vector4 S_BG_ROW = EditorPalette.collectionRow;
    private static readonly Vector4 S_BG_ROW_ALT = EditorPalette.collectionRowAlternate;
    private static readonly Vector4 S_BG_FIELD = new(0.235f, 0.22f, 0.27f, 1f);
    private static readonly Vector4 S_BORDER = new(0.31f, 0.30f, 0.35f, 1f);
    private static readonly Vector4 S_BORDER_SOFT = new(0.24f, 0.24f, 0.27f, 1f);
    private static readonly Vector4 S_TEXT = new(0.86f, 0.86f, 0.86f, 1f);
    private static readonly Vector4 S_TEXT_MUTED = new(0.54f, 0.54f, 0.56f, 1f);
    private static readonly Vector4 S_ACCENT = new(0.50f, 0.45f, 0.62f, 1f);
    #endregion

    #region State
    private float m_treeWidth = C_TREE_WIDTH;
    private string m_filter = string.Empty;
    private ViewMode m_viewMode = ViewMode.List;
    private EntryTypeFilter m_entryTypeFilter = EntryTypeFilter.All;
    private EntryScopeFilter m_entryScopeFilter = EntryScopeFilter.CurrentOnly;
    private float m_gridScale = C_GRID_SCALE_DEFAULT;
    private string m_historyCurrent = string.Empty;
    private readonly Stack<string> m_backHistory = [];
    private readonly Stack<string> m_forwardHistory = [];
    private bool m_treeRootOpenRequest = true;
    private bool m_treeCurrentDirectoryOpenRequest;
    private bool m_treeSelectedPathOpenRequest;
    private string m_treeCurrentDirectoryOpenTarget = string.Empty;
    private string m_treeSelectedPathOpenTarget = string.Empty;
    private string? m_lastTreeCurrentDirectoryOpenTarget;
    private string? m_lastTreeSelectedPathOpenTarget;
    #endregion

    #region Types
    private enum ViewMode
    {
        List,
        Grid
    }

    private enum EntryTypeFilter
    {
        All,
        FoldersOnly,
        FilesOnly
    }

    private enum EntryScopeFilter
    {
        CurrentOnly,
        Recursive
    }
    #endregion

    #region Lifecycle
    /// <summary>
    /// Creates the panel.
    /// </summary>
    public FileBrowserPanel()
        : base("asset.file-browser", "File")
    {
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
        SyncExternalDirectoryChange(context.selection.currentDirectory);

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
            PrepareTreeOpenRequests(context);
            DrawTreeEntry(context, string.Empty, "Assets", true);
            ClearTreeOpenRequests();
        }

        NativeImGui.EndChild();
        NativeImGui.PopStyleColor();
    }

    private void DrawContentPane(EditorContext context)
    {
        NativeImGui.PushStyleColor(ImGuiCol.ChildBg, S_BG);
        if (NativeImGui.BeginChild("##ContentPane", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            DrawToolbar(context);
            DrawEntriesRegion(context, CollectVisibleEntries(context));
        }

        NativeImGui.EndChild();
        NativeImGui.PopStyleColor();
    }
    #endregion

    #region Tree
    private void DrawTreeEntry(EditorContext context, string relativePath, string label, bool isRoot)
    {
        IReadOnlyList<AssetFileEntry> children = GetVisibleChildren(relativePath);
        List<AssetFileEntry> sorted = SortTreeEntries(children);

        bool isDirectory = isRoot || IsDirectoryPath(relativePath);
        bool selected = string.Equals(context.selection.selectedPath, relativePath, StringComparison.Ordinal);
        bool isLeaf = !isDirectory || sorted.Count == 0;
        bool isCurrentDirectory = isDirectory && string.Equals(context.selection.currentDirectory, relativePath, StringComparison.Ordinal);

        string icon = isDirectory ? ImGuiIcon.Folder : GetFileIcon(relativePath);
        string nodeId = $"tree_{(isRoot ? "root" : relativePath)}";
        if (ShouldOpenTreeEntry(relativePath, isRoot, isDirectory))
            ImGuiWidget.SetNextTreeNodeOpen(true);

        TreeNodeResult result = ImGuiWidget.TreeNode(
            nodeId,
            () => ImGuiWidget.IconText(icon, label, isCurrentDirectory),
            new TreeNodeOptions
            {
                selected = selected,
                isLeaf = isLeaf
            });

        if (!isRoot && AssetManager.TryGetFileSystemEntry(relativePath, out AssetFileEntry treeEntry))
        {
            DrawAssetDragSource(treeEntry);
        }

        if (result.isClicked || result.isDoubleClicked)
        {
            context.selection.SetSelectedPath(relativePath);
        }

        if (isDirectory && result.isDoubleClicked)
        {
            NavigateTo(context, relativePath, relativePath);
        }

        if (result.isOpen)
        {
            for (int i = 0; i < sorted.Count; i++)
            {
                AssetFileEntry child = sorted[i];
                DrawTreeEntry(context, child.relativePath, Path.GetFileName(child.relativePath), false);
            }

            NativeImGui.TreePop();
        }
    }

    private void PrepareTreeOpenRequests(EditorContext context)
    {
        string currentDirectory = NormalizePath(context.selection.currentDirectory);
        if (!m_treeCurrentDirectoryOpenRequest &&
            !string.Equals(m_lastTreeCurrentDirectoryOpenTarget, currentDirectory, StringComparison.Ordinal))
        {
            m_treeCurrentDirectoryOpenTarget = currentDirectory;
            m_treeCurrentDirectoryOpenRequest = true;
            m_lastTreeCurrentDirectoryOpenTarget = currentDirectory;
        }

        string selectedPath = NormalizePath(context.selection.selectedPath);
        string selectedTreePath = GetTreeRevealTarget(selectedPath);
        if (!m_treeSelectedPathOpenRequest &&
            !string.Equals(m_lastTreeSelectedPathOpenTarget, selectedTreePath, StringComparison.Ordinal))
        {
            m_treeSelectedPathOpenTarget = selectedTreePath;
            m_treeSelectedPathOpenRequest = true;
            m_lastTreeSelectedPathOpenTarget = selectedTreePath;
        }
    }

    private void ClearTreeOpenRequests()
    {
        m_treeRootOpenRequest = false;
        m_treeCurrentDirectoryOpenRequest = false;
        m_treeSelectedPathOpenRequest = false;
    }

    private void RequestRevealTreePath(string path)
    {
        RequestOpenTreeToPath(GetTreeRevealTarget(path));
    }

    private void RequestOpenTreeToPath(string path)
    {
        string normalizedPath = NormalizePath(path);
        string treePath = IsDirectoryPath(normalizedPath) ? normalizedPath : GetParentDirectory(normalizedPath);
        m_treeSelectedPathOpenTarget = treePath;
        m_treeSelectedPathOpenRequest = true;
        m_lastTreeSelectedPathOpenTarget = treePath;
    }

    private static string GetTreeRevealTarget(string path)
    {
        string normalizedPath = NormalizePath(path);
        return GetParentDirectory(normalizedPath);
    }

    private bool ShouldOpenTreeEntry(string relativePath, bool isRoot, bool isDirectory)
    {
        if (!isDirectory)
            return false;

        if (isRoot)
            return m_treeRootOpenRequest || m_treeCurrentDirectoryOpenRequest || m_treeSelectedPathOpenRequest;

        return (m_treeCurrentDirectoryOpenRequest && IsAncestorOrSelf(relativePath, m_treeCurrentDirectoryOpenTarget)) ||
               (m_treeSelectedPathOpenRequest && IsAncestorOrSelf(relativePath, m_treeSelectedPathOpenTarget));
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
        PushButtonColors(CanGoBack ? S_ACCENT : S_BORDER_SOFT);

        NativeImGui.BeginDisabled(!CanGoBack);
        if (NativeImGui.SmallButton($"{ImGuiIcon.AngleLeft}##Back"))
            GoBack(context);

        NativeImGui.EndDisabled();
        NativeImGui.PopStyleColor(3);

        NativeImGui.SameLine(0f, 2f);
        PushButtonColors(CanGoForward ? S_ACCENT : S_BORDER_SOFT);
        NativeImGui.BeginDisabled(!CanGoForward);
        if (NativeImGui.SmallButton($"{ImGuiIcon.AngleRight}##Forward"))
            GoForward(context);

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

        DrawEntryTypeFilterOption("All", EntryTypeFilter.All);
        DrawEntryTypeFilterOption("Folders", EntryTypeFilter.FoldersOnly);
        DrawEntryTypeFilterOption("Files", EntryTypeFilter.FilesOnly);
        NativeImGui.Separator();
        DrawEntryScopeFilterOption("Current", EntryScopeFilter.CurrentOnly);
        DrawEntryScopeFilterOption("Recursive", EntryScopeFilter.Recursive);

        NativeImGui.EndCombo();
    }

    private void DrawEntryTypeFilterOption(string label, EntryTypeFilter filter)
    {
        bool selected = m_entryTypeFilter == filter;
        if (NativeImGui.Checkbox(label, ref selected))
        {
            if (selected)
                m_entryTypeFilter = filter;
            else if (m_entryTypeFilter == filter)
                m_entryTypeFilter = EntryTypeFilter.All;
        }
    }

    private void DrawEntryScopeFilterOption(string label, EntryScopeFilter filter)
    {
        bool selected = m_entryScopeFilter == filter;
        if (NativeImGui.Checkbox(label, ref selected))
        {
            if (selected)
                m_entryScopeFilter = filter;
            else if (m_entryScopeFilter == filter)
                m_entryScopeFilter = EntryScopeFilter.CurrentOnly;
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
        string name = Path.GetFileName(entry.relativePath);
        bool selected = string.Equals(context.selection.selectedPath, entry.relativePath, StringComparison.Ordinal);

        NativeImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderHovered, Vector4.Zero);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderActive, Vector4.Zero);
        Vector2 iconTextPos = NativeImGui.GetCursorScreenPos();
        if (NativeImGui.Selectable($"##entry_{entry.relativePath}", selected, ImGuiSelectableFlags.SpanAllColumns))
        {
            context.selection.SetSelectedPath(entry.relativePath);
            RequestRevealTreePath(entry.relativePath);
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
        DrawAssetDragSource(entry);

        NativeImGui.SameLine(iconTextPos.X - NativeImGui.GetWindowPos().X, 0f);
        ImGuiWidget.IconText(icon, name, false);

        if (entry.isDirectory && itemHovered && NativeImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            OpenEntryFromList(context, entry);

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
            RequestRevealTreePath(entry.relativePath);
        }

        DrawAssetDragSource(entry);

        if (entry.isDirectory && NativeImGui.IsItemHovered() && NativeImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            OpenEntryFromList(context, entry);

        DrawGridItemVisual(icon, name, selected, m_gridScale);
        NativeImGui.PopID();
    }

    private static void DrawAssetDragSource(AssetFileEntry entry)
    {
        if (entry.isDirectory)
        {
            return;
        }

        _ = ImGuiWidget.DragDropSource<Guid>(
            C_ASSET_PAYLOAD,
            () => AssetManager.TryGetPersistentId(entry.relativePath, out Guid persistentId)
                ? persistentId
                : Guid.Empty,
            () => NativeImGui.TextUnformatted(Path.GetFileName(entry.relativePath)));
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
                    NavigateTo(context, path);
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

    #region Data
    private IReadOnlyList<AssetFileEntry> CollectVisibleEntries(EditorContext context)
    {
        List<AssetFileEntry> entries = [];

        if (m_entryScopeFilter == EntryScopeFilter.CurrentOnly)
        {
            IReadOnlyList<AssetFileEntry> children = GetVisibleChildren(context.selection.currentDirectory);
            for (int i = 0; i < children.Count; i++)
                entries.Add(children[i]);
        }
        else
        {
            CollectEntriesRecursive(context.selection.currentDirectory, entries);
        }

        ApplyTypeFilter(entries);
        ApplySearchFilter(entries);

        entries.Sort(static (a, b) =>
        {
            string aName = Path.GetFileName(a.relativePath);
            string bName = Path.GetFileName(b.relativePath);
            int byName = string.Compare(aName, bName, StringComparison.OrdinalIgnoreCase);
            if (byName != 0)
                return byName;
            return string.CompareOrdinal(a.relativePath, b.relativePath);
        });

        return entries;
    }

    private void ApplyTypeFilter(List<AssetFileEntry> entries)
    {
        switch (m_entryTypeFilter)
        {
            case EntryTypeFilter.FoldersOnly:
                entries.RemoveAll(static entry => !entry.isDirectory);
                break;
            case EntryTypeFilter.FilesOnly:
                entries.RemoveAll(static entry => entry.isDirectory);
                break;
        }
    }

    private void ApplySearchFilter(List<AssetFileEntry> entries)
    {
        string filter = m_filter.Trim();
        if (string.IsNullOrEmpty(filter))
            return;

        entries.RemoveAll(entry =>
            !Path.GetFileName(entry.relativePath).Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private static void CollectEntriesRecursive(string directory, List<AssetFileEntry> entries)
    {
        IReadOnlyList<AssetFileEntry> children = GetVisibleChildren(directory);
        for (int i = 0; i < children.Count; i++)
        {
            AssetFileEntry child = children[i];
            entries.Add(child);
            if (child.isDirectory)
                CollectEntriesRecursive(child.relativePath, entries);
        }
    }

    private static List<AssetFileEntry> SortTreeEntries(IReadOnlyList<AssetFileEntry> entries)
    {
        List<AssetFileEntry> sorted = new(entries.Count);
        for (int i = 0; i < entries.Count; i++)
            sorted.Add(entries[i]);

        sorted.Sort(static (a, b) =>
        {
            if (a.isDirectory != b.isDirectory)
                return a.isDirectory ? -1 : 1;
            return string.Compare(Path.GetFileName(a.relativePath), Path.GetFileName(b.relativePath), StringComparison.OrdinalIgnoreCase);
        });

        return sorted;
    }

    private static IReadOnlyList<AssetFileEntry> GetVisibleChildren(string relativePath)
    {
        IReadOnlyList<AssetFileEntry> children = AssetManager.GetFileSystemChildren(relativePath);
        var visible = new List<AssetFileEntry>(children.Count);
        for (int i = 0; i < children.Count; i++)
        {
            if (FileBrowserEntryFilter.IsVisible(children[i]))
                visible.Add(children[i]);
        }
        return visible;
    }
    #endregion

    #region Style
    private static void PushBrowserStyle()
    {
        NativeImGui.PushStyleColor(ImGuiCol.Text, S_TEXT);
        NativeImGui.PushStyleColor(ImGuiCol.WindowBg, S_BG);
        NativeImGui.PushStyleColor(ImGuiCol.ChildBg, S_BG);
        NativeImGui.PushStyleColor(ImGuiCol.Border, S_BORDER);
        NativeImGui.PushStyleColor(ImGuiCol.TableHeaderBg, S_BG);
        NativeImGui.PushStyleColor(ImGuiCol.TableBorderStrong, S_BORDER);
        NativeImGui.PushStyleColor(ImGuiCol.TableBorderLight, S_BORDER_SOFT);
        NativeImGui.PushStyleColor(ImGuiCol.TableRowBg, S_BG_ROW);
        NativeImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, S_BG_ROW_ALT);
        NativeImGui.PushStyleColor(ImGuiCol.Header, S_ACCENT);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderHovered, S_ACCENT);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderActive, S_ACCENT);
        NativeImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2f, 1f));
        NativeImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        NativeImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(5f, 2f));
        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2f, 2f));
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 1f);
    }

    private static void PopBrowserStyle()
    {
        NativeImGui.PopStyleVar(5);
        NativeImGui.PopStyleColor(12);
    }

    private static void PushButtonColors(Vector4 color)
    {
        NativeImGui.PushStyleColor(ImGuiCol.Button, color);
        NativeImGui.PushStyleColor(ImGuiCol.ButtonHovered, LerpColor(color, Vector4.One, 0.16f));
        NativeImGui.PushStyleColor(ImGuiCol.ButtonActive, LerpColor(color, Vector4.One, 0.24f));
    }

    private static Vector4 LerpColor(Vector4 a, Vector4 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Vector4(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            1f);
    }
    #endregion

    #region Navigation
    private bool CanGoBack => m_backHistory.Count > 0;

    private bool CanGoForward => m_forwardHistory.Count > 0;

    private void NavigateTo(EditorContext context, string directory, string? selectedPathAfterNavigation = null)
    {
        directory = NormalizePath(directory);
        if (string.Equals(context.selection.currentDirectory, directory, StringComparison.Ordinal))
            return;

        m_backHistory.Push(NormalizePath(context.selection.currentDirectory));
        m_forwardHistory.Clear();
        ApplyDirectory(context, directory);
        if (selectedPathAfterNavigation is not null)
            context.selection.SetSelectedPath(selectedPathAfterNavigation);
    }

    private void GoBack(EditorContext context)
    {
        if (m_backHistory.Count == 0)
            return;

        m_forwardHistory.Push(NormalizePath(context.selection.currentDirectory));
        ApplyDirectory(context, m_backHistory.Pop());
    }

    private void GoForward(EditorContext context)
    {
        if (m_forwardHistory.Count == 0)
            return;

        m_backHistory.Push(NormalizePath(context.selection.currentDirectory));
        ApplyDirectory(context, m_forwardHistory.Pop());
    }

    private void ApplyDirectory(EditorContext context, string directory)
    {
        m_historyCurrent = NormalizePath(directory);
        context.selection.SetCurrentDirectory(m_historyCurrent);
        context.selection.SetSelectedPath(string.Empty);
    }

    private void OpenEntryFromList(EditorContext context, AssetFileEntry entry)
    {
        if (entry.isDirectory)
        {
            RequestOpenTreeToPath(entry.relativePath);
            NavigateTo(context, entry.relativePath, entry.relativePath);
        }
    }

    private void SyncExternalDirectoryChange(string directory)
    {
        directory = NormalizePath(directory);
        if (string.Equals(m_historyCurrent, directory, StringComparison.Ordinal))
            return;

        m_historyCurrent = directory;
        m_backHistory.Clear();
        m_forwardHistory.Clear();
    }

    #endregion

    #region Helpers
    private static bool IsDirectoryPath(string relativePath)
    {
        return AssetManager.TryGetFileSystemEntry(relativePath, out AssetFileEntry entry) && entry.isDirectory;
    }

    private static string GetDirectoryLabel(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return "Assets";

        string name = Path.GetFileName(relativePath);
        return string.IsNullOrEmpty(name) ? "Assets" : name;
    }

    private static string GetSourceText(AssetFileEntry entry, string currentDirectory)
    {
        string? directory = Path.GetDirectoryName(entry.relativePath)?.Replace('\\', '/');
        currentDirectory = NormalizePath(currentDirectory);
        directory = string.IsNullOrEmpty(directory) ? string.Empty : NormalizePath(directory);

        if (string.Equals(directory, currentDirectory, StringComparison.Ordinal))
            return "~";

        if (string.IsNullOrEmpty(currentDirectory))
            return string.IsNullOrEmpty(directory) ? "~" : $"~/{directory}";

        string prefix = currentDirectory + "/";
        if (directory.StartsWith(prefix, StringComparison.Ordinal))
        {
            string relativeSource = directory[prefix.Length..];
            return string.IsNullOrEmpty(relativeSource) ? "~" : $"~/{relativeSource}";
        }

        return string.IsNullOrEmpty(directory) ? "~" : $"~/{directory}";
    }

    private static string GetTypeText(AssetFileEntry entry)
    {
        if (entry.isDirectory)
            return "FOLDER";

        string extension = entry.extension;
        if (string.IsNullOrEmpty(extension))
            extension = Path.GetExtension(entry.relativePath);

        return string.IsNullOrEmpty(extension) ? "FILE" : extension.TrimStart('.').ToUpperInvariant();
    }

    private static string GetFileIcon(string relativePath)
    {
        string extension = Path.GetExtension(relativePath);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            ? ImGuiIcon.FileImage
            : ImGuiIcon.File;
    }

    private static IReadOnlyList<(string Label, string Path)> BuildBreadcrumbParts(string relativePath)
    {
        List<(string Label, string Path)> parts = [("Assets", string.Empty)];
        if (string.IsNullOrEmpty(relativePath))
            return parts;

        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string path = string.Empty;
        for (int i = 0; i < segments.Length; i++)
        {
            path = string.IsNullOrEmpty(path) ? segments[i] : $"{path}/{segments[i]}";
            parts.Add((segments[i], path));
        }

        return parts;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        return path.Replace('\\', '/').Trim('/');
    }

    private static string GetParentDirectory(string relativePath)
    {
        string? directory = Path.GetDirectoryName(relativePath)?.Replace('\\', '/');
        return string.IsNullOrEmpty(directory) ? string.Empty : NormalizePath(directory);
    }

    private static string[] FitTextToLines(string text, float maxWidth, int maxLines)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLines, 1);
        if (string.IsNullOrEmpty(text))
            return [string.Empty];

        List<string> elements = [];
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            elements.Add(enumerator.GetTextElement());

        List<string> lines = new(maxLines);
        int offset = 0;
        for (int lineIndex = 0; lineIndex < maxLines && offset < elements.Count; lineIndex++)
        {
            string remaining = string.Concat(elements.GetRange(offset, elements.Count - offset));
            if (NativeImGui.CalcTextSize(remaining).X <= maxWidth)
            {
                lines.Add(remaining);
                break;
            }

            bool isLastLine = lineIndex == maxLines - 1;
            string suffix = isLastLine ? "..." : string.Empty;
            int count = 0;
            string candidate = string.Empty;
            while (offset + count < elements.Count)
            {
                string next = candidate + elements[offset + count];
                if (NativeImGui.CalcTextSize(next + suffix).X > maxWidth)
                    break;
                candidate = next;
                count++;
            }

            if (count == 0)
            {
                lines.Add(isLastLine && NativeImGui.CalcTextSize("...").X <= maxWidth
                    ? "..."
                    : elements[offset]);
                offset++;
                continue;
            }

            lines.Add(candidate + suffix);
            offset += count;
        }

        return lines.Count == 0 ? [string.Empty] : lines.ToArray();
    }

    private static bool IsAncestorOrSelf(string candidateAncestor, string path)
    {
        if (candidateAncestor.Length == 0)
            return true;

        if (string.Equals(candidateAncestor, path, StringComparison.Ordinal))
            return true;

        if (path.Length <= candidateAncestor.Length)
            return false;

        if (!path.StartsWith(candidateAncestor, StringComparison.Ordinal))
            return false;

        return path[candidateAncestor.Length] == '/';
    }
    #endregion
}
