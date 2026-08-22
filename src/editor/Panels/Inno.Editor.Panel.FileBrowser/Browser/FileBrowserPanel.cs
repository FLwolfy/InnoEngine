using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

using Inno.Assets;
using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using static Inno.Editor.Panel.FileBrowser.FileBrowserUtility;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.FileBrowser;

/// <summary>
/// Asset browser panel with a tree pane and filtered table view.
/// </summary>
[EditorPanel("asset.file-browser", "File", order: 300)]
public sealed class FileBrowserPanel : EditorPanel, IEditorWorkspaceState
{
    #region Constants
    private const int C_SEARCH_BUFFER_SIZE = 256;
    private const int C_GRID_LABEL_LINE_COUNT = 2;
    #endregion

    #region State
    private readonly AssetEditorModule m_assets;
    private readonly FileBrowserData m_data;
    private readonly FileBrowserNavigation m_navigation;
    private readonly FileBrowserDragDrop m_dragDrop;
    private readonly FileBrowserChangeTracker m_changeTracker;
    private readonly FileBrowserRename m_rename;
    private readonly FileBrowserContextMenu m_contextMenu;
    private readonly FileBrowserTree m_tree;

    private float m_treeWidth = EditorWidget.style.assetTreeWidth;
    private string m_filter = string.Empty;
    private ViewMode m_viewMode = ViewMode.List;
    private FileBrowserEntryTypeFilter m_entryTypeFilter = FileBrowserEntryTypeFilter.All;
    private FileBrowserEntryScopeFilter m_entryScopeFilter = FileBrowserEntryScopeFilter.CurrentOnly;
    private float m_gridScale = EditorWidget.style.assetGridDefaultScale;
    private float m_listNameSeparatorPosition = EditorWidget.style.assetListNameSeparatorPosition;
    private float m_listTypeSeparatorPosition = EditorWidget.style.assetListTypeSeparatorPosition;
    #endregion

    #region Types
    private enum ViewMode
    {
        List,
        Grid
    }

    #endregion

    /// <inheritdoc />
    public string workspaceStateId => "asset-browser-panel";

    /// <inheritdoc />
    public void CaptureWorkspaceState(EditorWorkspaceStateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Set("viewMode", m_viewMode.ToString());
        writer.Set("filter", m_filter);
        writer.Set("entryTypeFilter", m_entryTypeFilter.ToString());
        writer.Set("entryScopeFilter", m_entryScopeFilter.ToString());
        writer.Set("treeWidth", m_treeWidth);
        writer.Set("gridScale", m_gridScale);
        writer.Set("listNameSeparator", m_listNameSeparatorPosition);
        writer.Set("listTypeSeparator", m_listTypeSeparatorPosition);
    }

    /// <inheritdoc />
    public void RestoreWorkspaceState(EditorWorkspaceStateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (Enum.TryParse(reader.Get("viewMode", string.Empty), out ViewMode viewMode))
            m_viewMode = viewMode;
        if (Enum.TryParse(reader.Get("entryTypeFilter", string.Empty), out FileBrowserEntryTypeFilter typeFilter))
            m_entryTypeFilter = typeFilter;
        if (Enum.TryParse(reader.Get("entryScopeFilter", string.Empty), out FileBrowserEntryScopeFilter scopeFilter))
            m_entryScopeFilter = scopeFilter;
        m_filter = reader.Get("filter", string.Empty);
        float restoredTreeWidth = reader.Get("treeWidth", EditorWidget.style.assetTreeWidth);
        m_treeWidth = float.IsFinite(restoredTreeWidth)
            ? MathF.Max(0f, restoredTreeWidth)
            : EditorWidget.style.assetTreeWidth;
        m_gridScale = Math.Clamp(
            reader.Get("gridScale", EditorWidget.style.assetGridDefaultScale),
            EditorWidget.style.assetGridMinimumScale,
            EditorWidget.style.assetGridMaximumScale);
        SetListColumnSeparators(
            reader.Get("listNameSeparator", EditorWidget.style.assetListNameSeparatorPosition),
            reader.Get("listTypeSeparator", EditorWidget.style.assetListTypeSeparatorPosition));
    }

    #region Lifecycle
    /// <summary>
    /// Creates the panel.
    /// </summary>
    internal FileBrowserPanel(AssetEditorModule assets)
    {
        m_assets = assets;
        m_data = new FileBrowserData(assets);
        m_navigation = new FileBrowserNavigation(assets);
        m_dragDrop = new FileBrowserDragDrop(assets);
        m_changeTracker = new FileBrowserChangeTracker(assets);
        m_rename = new FileBrowserRename(assets);
        m_contextMenu = new FileBrowserContextMenu(assets, m_rename);
        m_tree = new FileBrowserTree(
            m_data,
            m_navigation,
            m_dragDrop,
            m_rename,
            m_contextMenu,
            assets);
    }

    /// <inheritdoc />
    protected override void OnAttach(EditorContext context)
    {
        m_changeTracker.Attach(context);
    }

    /// <inheritdoc />
    protected override void OnDetach(EditorContext context)
    {
        m_changeTracker.Detach();
    }

    /// <inheritdoc />
    public override void Draw(EditorContext context)
    {
        m_rename.Update(context);
        PushBrowserStyle();
        DrawBrowser(context);
        PopBrowserStyle();
    }
    #endregion

    #region Layout
    private void DrawBrowser(EditorContext context)
    {
        m_navigation.SyncExternalDirectoryChange(m_assets.browser.currentDirectory);

        ImGuiStylePtr style = NativeImGui.GetStyle();
        float breadcrumbBarHeight = GetBreadcrumbBarHeight(m_assets.browser.currentDirectory);
        Vector2 bodySize = new(0f, -(breadcrumbBarHeight + style.ItemSpacing.Y));
        if (NativeImGui.BeginChild("##FileBrowserMain", bodySize, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGuiTableFlags splitFlags =
                ImGuiTableFlags.NoPadOuterX |
                ImGuiTableFlags.NoKeepColumnsVisible |
                ImGuiTableFlags.SizingFixedFit |
                ImGuiTableFlags.NoSavedSettings;

            float splitterWidth = GetTreeSplitterWidth(style);
            float availableWidth = MathF.Max(0f, NativeImGui.GetContentRegionAvail().X);
            m_treeWidth = ClampTreeWidth(m_treeWidth, availableWidth, splitterWidth);
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
                DrawTreeSplitter(splitterWidth, availableWidth);

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
        return MathF.Max(EditorWidget.style.assetSplitterMinimumWidth, style.DockingSeparatorSize);
    }

    private void DrawTreeSplitter(float width, float availableWidth)
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
                m_treeWidth = ClampTreeWidth(m_treeWidth + delta.X, availableWidth, width);
                NativeImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
            }
        }

        Vector2 min = NativeImGui.GetItemRectMin();
        Vector2 max = NativeImGui.GetItemRectMax();
        Vector4 color = active ? EditorPalette.assetAccent : hovered ? EditorPalette.assetBorder : EditorPalette.assetBorderSoft;
        NativeImGui.AddRectFilled(NativeImGui.GetWindowDrawList(), min, max, NativeImGui.ColorConvertFloat4ToU32(color));
    }

    private static float ClampTreeWidth(
        float requestedWidth,
        float availableWidth,
        float splitterWidth)
    {
        float combinedPaneWidth = MathF.Max(0f, availableWidth - splitterWidth);
        float minimumPaneWidth = MathF.Min(
            EditorWidget.style.assetPaneMinimumVisibleWidth,
            combinedPaneWidth * 0.5f);
        float maximumTreeWidth = MathF.Max(minimumPaneWidth, combinedPaneWidth - minimumPaneWidth);
        return Math.Clamp(requestedWidth, minimumPaneWidth, maximumTreeWidth);
    }

    private void DrawTreePane(EditorContext context)
    {
        NativeImGui.PushStyleColor(ImGuiCol.ChildBg, EditorPalette.collectionHeader);
        ImGuiStylePtr style = NativeImGui.GetStyle();
        Vector2 treePaneSize = new(-style.WindowPadding.X, 0f);
        if (NativeImGui.BeginChild("##TreePane", treePaneSize, ImGuiChildFlags.None))
        {
            m_tree.PrepareOpenRequests(context);
            m_tree.DrawEntry(context, string.Empty, "Assets", true);
            m_tree.ClearOpenRequests();
            HandleBackgroundSelection(context);
            m_contextMenu.DrawBackground(
                context,
                "##asset_tree_background_context",
                FileBrowserPresentation.Tree);
        }

        NativeImGui.EndChild();
        m_dragDrop.DrawDirectoryTarget(context);
        NativeImGui.PopStyleColor();
    }

    private void DrawContentPane(EditorContext context)
    {
        NativeImGui.PushStyleColor(ImGuiCol.ChildBg, EditorPalette.collectionHeader);
        if (NativeImGui.BeginChild("##ContentPane", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            DrawToolbar(context);
            DrawEntriesRegion(
                context,
                m_data.CollectVisibleEntries(context, m_entryTypeFilter, m_entryScopeFilter, m_filter));
        }

        NativeImGui.EndChild();
        m_dragDrop.DrawDirectoryTarget(context);
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
        string current = m_assets.browser.currentDirectory;

        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidget.style.breadcrumbFramePadding);
        bool canGoBack = m_navigation.canGoBack;
        PushButtonColors(canGoBack ? EditorPalette.assetAccent : EditorPalette.assetBorderSoft);

        NativeImGui.BeginDisabled(!canGoBack);
        if (NativeImGui.SmallButton($"{ImGuiIcon.AngleLeft}##Back"))
            m_navigation.GoBack(context);

        NativeImGui.EndDisabled();
        NativeImGui.PopStyleColor(3);

        NativeImGui.SameLine(0f, EditorWidget.style.assetToolbarTightSpacing);
        bool canGoForward = m_navigation.canGoForward;
        PushButtonColors(canGoForward ? EditorPalette.assetAccent : EditorPalette.assetBorderSoft);
        NativeImGui.BeginDisabled(!canGoForward);
        if (NativeImGui.SmallButton($"{ImGuiIcon.AngleRight}##Forward"))
            m_navigation.GoForward(context);

        NativeImGui.EndDisabled();
        NativeImGui.PopStyleColor(3);

        NativeImGui.SameLine(0f, EditorWidget.style.assetToolbarSectionSpacing);

        NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.assetText);
        NativeImGui.TextUnformatted(GetDirectoryLabel(current));
        NativeImGui.PopStyleColor();

        NativeImGui.PopStyleVar();
    }

    private void DrawViewAndSearchBar()
    {
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidget.style.toolbarFramePadding);

        PushButtonColors(EditorPalette.assetAccent);
        if (NativeImGui.SmallButton($"{m_viewMode}##ViewMode"))
            m_viewMode = m_viewMode == ViewMode.List ? ViewMode.Grid : ViewMode.List;
        NativeImGui.PopStyleColor(3);

        NativeImGui.SameLine(0f, EditorWidget.style.assetToolbarSpacing);
        DrawEntryFilterCombo();
        NativeImGui.SameLine(0f, EditorWidget.style.assetToolbarSpacing);

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
        {
            DrawEntriesTable(context, entries, m_assets.browser.currentDirectory);
            HandleBackgroundSelection(context);
            m_contextMenu.DrawBackground(
                context,
                "##asset_list_background_context",
                FileBrowserPresentation.List);
        }

        NativeImGui.EndChild();
    }

    private void DrawGridRegion(EditorContext context, IReadOnlyList<AssetFileEntry> entries)
    {
        ImGuiStylePtr style = NativeImGui.GetStyle();
        float sliderHeight = NativeImGui.GetFrameHeight() + style.WindowPadding.Y * 2f + style.ItemSpacing.Y;
        if (NativeImGui.BeginChild("##EntriesScroll", new Vector2(0f, -sliderHeight), ImGuiChildFlags.None))
        {
            DrawGrid(context, entries);
            HandleBackgroundSelection(context);
            m_contextMenu.DrawBackground(
                context,
                "##asset_grid_background_context",
                FileBrowserPresentation.Grid);
        }

        NativeImGui.EndChild();
        DrawGridScaleSlider();
    }

    private void HandleBackgroundSelection(EditorContext context)
    {
        if (!NativeImGui.IsWindowHovered() ||
            NativeImGui.IsAnyItemHovered() ||
            !NativeImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            return;
        }

        m_assets.browser.Select(context, null);
    }

    private void DrawGridScaleSlider()
    {
        m_gridScale = Math.Clamp(m_gridScale, EditorWidget.style.assetGridMinimumScale, EditorWidget.style.assetGridMaximumScale);
        DrawGridScaleTopSplitter();
        Vector2 cursor = NativeImGui.GetCursorPos();
        float labelOffsetY = MathF.Max(0f, (NativeImGui.GetFrameHeight() - NativeImGui.GetTextLineHeight()) * 0.5f);
        NativeImGui.SetCursorPosY(cursor.Y + labelOffsetY);
        NativeImGui.TextUnformatted("Scale");
        NativeImGui.SameLine();
        NativeImGui.SetCursorPosY(cursor.Y);
        NativeImGui.SetNextItemWidth(-1f);
        _ = NativeImGui.SliderFloat("##GridScale", ref m_gridScale, EditorWidget.style.assetGridMinimumScale, EditorWidget.style.assetGridMaximumScale, "%.1f");
        m_gridScale = Math.Clamp(m_gridScale, EditorWidget.style.assetGridMinimumScale, EditorWidget.style.assetGridMaximumScale);
    }

    private static void DrawGridScaleTopSplitter()
    {
        ImGuiStylePtr style = NativeImGui.GetStyle();
        Vector2 cursor = NativeImGui.GetCursorScreenPos();
        float width = NativeImGui.GetContentRegionAvail().X;
        float lineY = cursor.Y + style.WindowPadding.Y;
        uint color = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.assetBorder);
        NativeImGui.GetWindowDrawList().AddLine(
            new Vector2(cursor.X, lineY),
            new Vector2(cursor.X + width, lineY),
            color,
            EditorWidget.style.borderSize);
        NativeImGui.SetCursorPosY(NativeImGui.GetCursorPosY() + style.WindowPadding.Y * 2f);
    }

    private void DrawEntriesTable(EditorContext context, IReadOnlyList<AssetFileEntry> entries, string currentDirectory)
    {
        ImGuiTableFlags flags =
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.NoPadOuterX |
            ImGuiTableFlags.SizingStretchProp |
            ImGuiTableFlags.NoSavedSettings;

        ImGuiStylePtr style = NativeImGui.GetStyle();
        Vector2 tableOrigin = NativeImGui.GetCursorScreenPos();
        Vector2 tableSize = NativeImGui.GetContentRegionAvail();
        ListColumnSeparatorState separators = HandleListColumnSeparators(tableOrigin, tableSize);
        NativeImGui.SetCursorScreenPos(tableOrigin);
        NativeImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(style.WindowPadding.X, style.CellPadding.Y));
        if (!NativeImGui.BeginTable("##FileBrowserEntries", 3, flags, new Vector2(0f, 0f)))
        {
            NativeImGui.Dummy(Vector2.Zero);
            NativeImGui.PopStyleVar();
            return;
        }

        NativeImGui.TableSetupColumn(
            "Name",
            ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoResize,
            m_listNameSeparatorPosition);
        NativeImGui.TableSetupColumn(
            "Type",
            ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoResize,
            m_listTypeSeparatorPosition - m_listNameSeparatorPosition);
        NativeImGui.TableSetupColumn(
            "Source",
            ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoResize,
            1f - m_listTypeSeparatorPosition);
        DrawHeaderRow();

        uint rowBg = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.collectionRow);
        uint rowAltBg = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.collectionRowAlternate);
        for (int i = 0; i < entries.Count; i++)
        {
            AssetFileEntry entry = entries[i];
            if (i > 0)
                NativeImGui.TableNextRow(ImGuiTableRowFlags.None, EditorWidget.style.assetListRowSpacing);
            NativeImGui.TableNextRow();
            NativeImGui.TableSetBgColor(
                ImGuiTableBgTarget.RowBg0,
                i % 2 == 0 ? rowBg : rowAltBg);

            DrawNameCell(context, entry);
            DrawTextCell(GetTypeText(entry), EditorPalette.assetText);
            DrawTextCell(GetSourceText(entry, currentDirectory), EditorPalette.assetText);
        }

        NativeImGui.EndTable();
        DrawListColumnSeparators(tableOrigin, tableSize.X, separators);
        NativeImGui.PopStyleVar();
    }

    private ListColumnSeparatorState HandleListColumnSeparators(Vector2 origin, Vector2 size)
    {
        float width = MathF.Max(1f, size.X);
        float height = MathF.Max(1f, size.Y);
        float hitWidth = EditorWidget.style.assetListSeparatorHitWidth;
        bool nameHovered;
        bool nameActive;
        bool typeHovered;
        bool typeActive;

        float nameX = origin.X + width * m_listNameSeparatorPosition;
        NativeImGui.SetCursorScreenPos(new Vector2(nameX - hitWidth * 0.5f, origin.Y));
        _ = NativeImGui.InvisibleButton("##AssetListNameSeparator", new Vector2(hitWidth, height));
        nameHovered = NativeImGui.IsItemHovered();
        nameActive = NativeImGui.IsItemActive();
        if (nameHovered || nameActive)
            NativeImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

        if (nameActive)
        {
            float requested = (NativeImGui.GetMousePos().X - origin.X) / width;
            SetListColumnSeparators(requested, m_listTypeSeparatorPosition);
        }

        float typeX = origin.X + width * m_listTypeSeparatorPosition;
        NativeImGui.SetCursorScreenPos(new Vector2(typeX - hitWidth * 0.5f, origin.Y));
        _ = NativeImGui.InvisibleButton("##AssetListTypeSeparator", new Vector2(hitWidth, height));
        typeHovered = NativeImGui.IsItemHovered();
        typeActive = NativeImGui.IsItemActive();
        if (typeHovered || typeActive)
            NativeImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

        if (typeActive)
        {
            float requested = (NativeImGui.GetMousePos().X - origin.X) / width;
            SetListColumnSeparators(m_listNameSeparatorPosition, requested);
        }

        return new ListColumnSeparatorState(nameHovered, nameActive, typeHovered, typeActive);
    }

    private void DrawListColumnSeparators(
        Vector2 origin,
        float width,
        ListColumnSeparatorState state)
    {
        float bottom = MathF.Max(origin.Y, NativeImGui.GetCursorScreenPos().Y);
        DrawListColumnSeparator(
            origin.X + width * m_listNameSeparatorPosition,
            origin.Y,
            bottom,
            state.nameHovered,
            state.nameActive);
        DrawListColumnSeparator(
            origin.X + width * m_listTypeSeparatorPosition,
            origin.Y,
            bottom,
            state.typeHovered,
            state.typeActive);
    }

    private static void DrawListColumnSeparator(
        float x,
        float top,
        float bottom,
        bool hovered,
        bool active)
    {
        Vector4 color = active
            ? EditorPalette.assetAccent
            : hovered
                ? EditorPalette.assetBorder
                : EditorPalette.assetBorderSoft;
        NativeImGui.GetWindowDrawList().AddLine(
            new Vector2(x, top),
            new Vector2(x, bottom),
            NativeImGui.ColorConvertFloat4ToU32(color),
            EditorWidget.style.borderSize);
    }

    private void SetListColumnSeparators(float namePosition, float typePosition)
    {
        float minimum = EditorWidget.style.assetListMinimumColumnRatio;
        m_listNameSeparatorPosition = Math.Clamp(
            namePosition,
            minimum,
            1f - minimum * 2f);
        m_listTypeSeparatorPosition = Math.Clamp(
            typePosition,
            m_listNameSeparatorPosition + minimum,
            1f - minimum);
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
        InsetListCellContent();
        NativeImGui.TextUnformatted("Name");
        _ = NativeImGui.TableSetColumnIndex(1);
        InsetListCellContent();
        NativeImGui.TextUnformatted("Type");
        _ = NativeImGui.TableSetColumnIndex(2);
        InsetListCellContent();
        NativeImGui.TextUnformatted("Source");
    }

    private void DrawNameCell(EditorContext context, AssetFileEntry entry)
    {
        _ = NativeImGui.TableSetColumnIndex(0);
        string icon = m_assets.ResolveIcon(entry);
        string name = entry.nameWithoutExtension;
        bool selected = string.Equals(m_assets.browser.GetSelectedPath(context), entry.relativePath, StringComparison.Ordinal);
        bool editing = m_rename.IsEditing(context, entry.relativePath, FileBrowserPresentation.List);

        InsetListCellContent();
        NativeImGui.PushStyleColor(ImGuiCol.Header, EditorPalette.transparent);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderHovered, EditorPalette.transparent);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderActive, EditorPalette.transparent);
        Vector2 iconTextPos = NativeImGui.GetCursorScreenPos();
        ImGuiSelectableFlags selectableFlags =
            ImGuiSelectableFlags.SpanAllColumns |
            ImGuiSelectableFlags.AllowDoubleClick;
        if (editing)
            selectableFlags |= ImGuiSelectableFlags.AllowOverlap;
        bool activated = NativeImGui.Selectable(
            $"##entry_{entry.relativePath}",
            selected,
            selectableFlags);
        bool itemHovered = NativeImGui.IsItemHovered();
        bool doubleClicked = itemHovered && NativeImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
        if (activated)
        {
            HandleEntryActivation(
                context,
                entry,
                FileBrowserPresentation.List,
                selected,
                doubleClicked);
        }
        m_rename.TryBeginDelayed(
            context,
            entry.relativePath,
            FileBrowserPresentation.List);

        bool itemActive = NativeImGui.IsItemActive();
        if (!editing)
        {
            m_contextMenu.DrawEntry(
                context,
                $"##asset_context_{entry.relativePath}",
                entry.relativePath,
                FileBrowserPresentation.List);
        }
        if (selected || itemHovered)
        {
            Vector4 highlight = itemActive
                ? EditorPalette.GetHovered(EditorPalette.assetAccent)
                : EditorPalette.assetAccent;
            NativeImGui.TableSetBgColor(
                ImGuiTableBgTarget.RowBg1,
                NativeImGui.ColorConvertFloat4ToU32(highlight));
        }
        if (!editing)
            m_dragDrop.DrawAssetSource(context, entry);

        NativeImGui.SameLine(iconTextPos.X - NativeImGui.GetWindowPos().X, 0f);
        if (editing)
        {
            EditorWidget.IconText(icon, string.Empty, false);
            NativeImGui.SameLine(0f, 0f);
            m_rename.Draw(
                context,
                $"list_{entry.relativePath}",
                entry.relativePath,
                FileBrowserPresentation.List,
                NativeImGui.GetContentRegionAvail().X);
        }
        else
        {
            EditorWidget.IconText(icon, name, false);
        }

        NativeImGui.PopStyleColor(3);
    }

    private static void DrawTextCell(string text, Vector4 color)
    {
        NativeImGui.TableNextColumn();
        InsetListCellContent();
        NativeImGui.PushStyleColor(ImGuiCol.Text, color);
        NativeImGui.TextUnformatted(text);
        NativeImGui.PopStyleColor();
    }

    private static void InsetListCellContent()
    {
        NativeImGui.SetCursorPosX(
            NativeImGui.GetCursorPosX() + EditorWidget.style.assetListContentHorizontalPadding);
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
        string icon = m_assets.ResolveIcon(entry);
        string name = Path.GetFileName(entry.relativePath);
        bool selected = string.Equals(m_assets.browser.GetSelectedPath(context), entry.relativePath, StringComparison.Ordinal);
        bool editing = m_rename.IsEditing(context, entry.relativePath, FileBrowserPresentation.Grid);
        Vector2 itemSize = new(cellSize - EditorWidget.style.assetGridCellPadding, cellSize - EditorWidget.style.assetGridCellPadding);

        NativeImGui.PushID(entry.relativePath);
        NativeImGui.PushStyleColor(ImGuiCol.Header, EditorPalette.transparent);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderHovered, EditorPalette.transparent);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderActive, EditorPalette.transparent);
        ImGuiSelectableFlags selectableFlags = ImGuiSelectableFlags.AllowDoubleClick;
        if (editing)
            selectableFlags |= ImGuiSelectableFlags.AllowOverlap;
        bool activated = NativeImGui.Selectable(
            "##GridItem",
            selected,
            selectableFlags,
            itemSize);
        bool itemHovered = NativeImGui.IsItemHovered();
        bool itemActive = NativeImGui.IsItemActive();
        Vector2 itemMin = NativeImGui.GetItemRectMin();
        Vector2 itemMax = NativeImGui.GetItemRectMax();
        Vector2 layoutCursor = NativeImGui.GetCursorScreenPos();
        bool doubleClicked = itemHovered &&
                             NativeImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
        if (activated)
        {
            HandleEntryActivation(
                context,
                entry,
                FileBrowserPresentation.Grid,
                selected,
                doubleClicked);
        }
        m_rename.TryBeginDelayed(
            context,
            entry.relativePath,
            FileBrowserPresentation.Grid);

        if (!editing)
        {
            m_contextMenu.DrawEntry(
                context,
                "##asset_grid_context",
                entry.relativePath,
                FileBrowserPresentation.Grid);
            m_dragDrop.DrawAssetSource(context, entry);
        }
        DrawGridItemVisual(
            icon,
            name,
            selected,
            m_gridScale,
            itemHovered,
            itemActive,
            itemMin,
            itemMax,
            drawName: !editing);
        if (editing)
        {
            float width = MathF.Max(
                1f,
                itemMax.X - itemMin.X - EditorWidget.style.assetGridLabelHorizontalPadding);
            float lineHeight = NativeImGui.GetTextLineHeight();
            float lineAdvance = MathF.Max(
                1f,
                lineHeight + EditorWidget.style.assetGridLabelLineSpacing);
            float labelAreaHeight = lineHeight +
                                    lineAdvance * (C_GRID_LABEL_LINE_COUNT - 1);
            float labelAreaTop = itemMax.Y -
                                 EditorWidget.style.assetGridLabelBottomPadding -
                                 labelAreaHeight;
            float y = labelAreaTop +
                      (labelAreaHeight - lineHeight) * 0.5f -
                      EditorWidget.style.inlineRenameVerticalInset;
            NativeImGui.SetCursorScreenPos(new Vector2(
                itemMin.X + EditorWidget.style.assetGridLabelHorizontalPadding * 0.5f,
                y));
            m_rename.Draw(
                context,
                $"grid_{entry.relativePath}",
                entry.relativePath,
                FileBrowserPresentation.Grid,
                width);
            NativeImGui.SetCursorScreenPos(layoutCursor);
            NativeImGui.Dummy(Vector2.Zero);
        }
        NativeImGui.PopStyleColor(3);
        NativeImGui.PopID();
    }

    private void HandleEntryActivation(
        EditorContext context,
        AssetFileEntry entry,
        FileBrowserPresentation presentation,
        bool wasSelected,
        bool doubleClicked)
    {
        if (m_rename.HandleActivation(
                context,
                entry.relativePath,
                presentation,
                wasSelected,
                doubleClicked))
        {
            m_navigation.OpenEntry(context, entry, m_tree);
            return;
        }

        m_assets.browser.Select(context, entry.relativePath);
        m_tree.RequestRevealPath(entry.relativePath);
    }

    private static void DrawGridItemVisual(
        string icon,
        string name,
        bool selected,
        float scale,
        bool hovered,
        bool active,
        Vector2 min,
        Vector2 max,
        bool drawName)
    {
        Vector2 size = max - min;

        Vector4 bg = selected ? EditorPalette.assetAccent : EditorPalette.collectionHeader;
        if (active)
            bg = EditorPalette.GetActive(bg);
        else if (hovered)
            bg = EditorPalette.GetHovered(bg);

        uint bgColor = NativeImGui.ColorConvertFloat4ToU32(bg);
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, bgColor, EditorWidget.style.assetFrameRounding);

        ImFontPtr font = NativeImGui.GetFont();
        float fontSize = NativeImGui.GetFontSize();
        float iconFontSize = fontSize * scale;
        Vector4 iconBounds = EditorWidget.GetGlyphVisualBounds(font, iconFontSize, icon);
        Vector2 iconSize = new(
            iconBounds.Z - iconBounds.X,
            iconBounds.W - iconBounds.Y);
        string[] nameLines = FitTextToLines(
            name,
            MathF.Max(1f, size.X - EditorWidget.style.assetGridLabelHorizontalPadding),
            C_GRID_LABEL_LINE_COUNT);
        float lineHeight = NativeImGui.GetTextLineHeight();
        float lineAdvance = MathF.Max(
            1f,
            lineHeight + EditorWidget.style.assetGridLabelLineSpacing);
        float labelAreaHeight = lineHeight +
                                lineAdvance * (C_GRID_LABEL_LINE_COUNT - 1);
        float labelAreaTop = max.Y -
                             EditorWidget.style.assetGridLabelBottomPadding -
                             labelAreaHeight;
        float labelHeight = lineHeight +
                            lineAdvance * Math.Max(0, nameLines.Length - 1);
        float labelY = labelAreaTop + (labelAreaHeight - labelHeight) * 0.5f;
        float iconAreaTop = min.Y + EditorWidget.style.assetGridIconTopPadding;
        float iconAreaBottom = MathF.Max(
            iconAreaTop + 1f,
            labelAreaTop - EditorWidget.style.assetGridIconLabelSpacing);
        float maximumIconWidth = MathF.Max(
            1f,
            size.X - EditorWidget.style.assetGridIconHorizontalPadding * 2f);
        float maximumIconHeight = MathF.Max(1f, iconAreaBottom - iconAreaTop);
        float fit = MathF.Min(
            1f,
            MathF.Min(
                maximumIconWidth / MathF.Max(1f, iconSize.X),
                maximumIconHeight / MathF.Max(1f, iconSize.Y)));
        if (fit < 1f)
        {
            iconFontSize *= fit;
            iconBounds = EditorWidget.GetGlyphVisualBounds(font, iconFontSize, icon);
            iconSize = new(
                iconBounds.Z - iconBounds.X,
                iconBounds.W - iconBounds.Y);
        }

        Vector2 iconAreaCenter = new(
            min.X + size.X * 0.5f,
            iconAreaTop + maximumIconHeight * 0.5f);

        uint textColor = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.assetText);
        NativeImGui.PushClipRect(min, max, true);
        EditorWidget.AddGlyphCentered(
            drawList,
            font,
            iconFontSize,
            icon,
            iconAreaCenter,
            textColor);
        for (int i = 0; drawName && i < nameLines.Length; i++)
        {
            Vector2 lineSize = NativeImGui.CalcTextSize(nameLines[i]);
            Vector2 linePosition = new(
                min.X + (size.X - lineSize.X) * 0.5f,
                labelY + lineAdvance * i);
            NativeImGui.AddText(drawList, linePosition, textColor, nameLines[i]);
        }
        NativeImGui.PopClipRect();
    }

    private float GetGridCellSize()
    {
        float fontSize = NativeImGui.GetFontSize();
        return MathF.Max(
            EditorWidget.style.assetGridMinimumCellSize,
            fontSize * (m_gridScale + EditorWidget.style.assetGridScaleBias) +
            EditorWidget.style.assetGridFixedCellPadding);
    }

    private readonly record struct ListColumnSeparatorState(
        bool nameHovered,
        bool nameActive,
        bool typeHovered,
        bool typeActive);

    private void DrawSearchInput()
    {
        _ = EditorWidget.SearchInput(
            "AssetSearch",
            ImGuiIcon.MagnifyingGlass,
            ref m_filter,
            (nuint)C_SEARCH_BUFFER_SIZE);
    }
    #endregion

    #region Bottom Bar
    private void DrawBreadcrumbBar(EditorContext context, float height)
    {
        IReadOnlyList<(string Label, string Path)> parts = BuildBreadcrumbParts(m_assets.browser.currentDirectory);
        Vector2 framePadding = EditorWidget.style.breadcrumbFramePadding;
        float contentWidth = CalculateBreadcrumbContentWidth(parts, framePadding);
        NativeImGui.SetNextWindowContentSize(new Vector2(MathF.Max(contentWidth, NativeImGui.GetContentRegionAvail().X), 0f));
        NativeImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (NativeImGui.BeginChild("##BreadcrumbBar", new Vector2(0f, height), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            DrawBreadcrumbTopSeparator();
            float contentHeight = contentWidth > NativeImGui.GetWindowSize().X ? EditorWidget.style.assetBreadcrumbHeight : height;
            float itemHeight = EditorWidget.GetClickableTextSize("A", framePadding).Y;
            NativeImGui.SetCursorPosY(MathF.Max(0f, (contentHeight - itemHeight) * 0.5f));
            NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.assetBreadcrumbText);

            for (int i = 0; i < parts.Count; i++)
            {
                (string label, string path) = parts[i];
                if (i > 0)
                {
                    NativeImGui.SameLine(0f, EditorWidget.style.assetBreadcrumbSpacing);
                    EditorWidget.CenteredText(">", new Vector2(
                        NativeImGui.CalcTextSize(">").X,
                        itemHeight));
                    NativeImGui.SameLine(0f, EditorWidget.style.assetBreadcrumbSpacing);
                }

                Vector2 itemSize = EditorWidget.GetClickableTextSize(label, framePadding);
                if (EditorWidget.ClickableText($"crumb_{path}", label, itemSize))
                    m_navigation.NavigateTo(context, path);
            }

            NativeImGui.PopStyleColor();
        }

        NativeImGui.EndChild();
        NativeImGui.PopStyleVar();
    }

    private static float GetBreadcrumbBarHeight(string currentDirectory)
    {
        IReadOnlyList<(string Label, string Path)> parts = BuildBreadcrumbParts(currentDirectory);
        float contentWidth = CalculateBreadcrumbContentWidth(
            parts,
            EditorWidget.style.breadcrumbFramePadding);
        return contentWidth > NativeImGui.GetContentRegionAvail().X
            ? EditorWidget.style.assetBreadcrumbHeight + NativeImGui.GetStyle().ScrollbarSize
            : EditorWidget.style.assetBreadcrumbHeight;
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
                width += separatorWidth + EditorWidget.style.assetBreadcrumbSpacing * 2f;

            width += NativeImGui.CalcTextSize(parts[i].Label).X + framePadding.X * 2f;
        }

        return MathF.Ceiling(width);
    }

    private static void DrawBreadcrumbTopSeparator()
    {
        Vector2 min = NativeImGui.GetWindowPos();
        Vector2 size = NativeImGui.GetWindowSize();
        uint color = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.assetBorder);
        NativeImGui.GetWindowDrawList().AddLine(
            min,
            new Vector2(min.X + size.X, min.Y),
            color,
            EditorWidget.style.borderSize);
    }
    #endregion

}
