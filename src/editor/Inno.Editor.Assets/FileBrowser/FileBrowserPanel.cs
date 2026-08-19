using Inno.Editor.Assets;

using Inno.Editor.Assets.Selection;

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

using Inno.Assets;
using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.Assets.AssetEditors;
using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;
using Inno.Editor.Core.Panels;
using Inno.Editor.ImGui;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using static Inno.Editor.Assets.FileBrowser.FileBrowserUtility;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Assets.FileBrowser;

/// <summary>
/// Asset browser panel with a tree pane and filtered table view.
/// </summary>
[EditorPanel("asset.file-browser", "File", order: 300)]
public sealed class FileBrowserPanel : EditorPanel
{
    #region Constants
    private const int C_SEARCH_BUFFER_SIZE = 256;
    #endregion

    #region State
    private readonly AssetEditorModule m_assets;
    private readonly FileBrowserData m_data;
    private readonly FileBrowserNavigation m_navigation;
    private readonly FileBrowserDragDrop m_dragDrop;
    private readonly FileBrowserChangeTracker m_changeTracker;
    private readonly FileBrowserTree m_tree;

    private float m_treeWidth = ImGuiWidget.style.assetTreeWidth;
    private string m_filter = string.Empty;
    private ViewMode m_viewMode = ViewMode.List;
    private FileBrowserEntryTypeFilter m_entryTypeFilter = FileBrowserEntryTypeFilter.All;
    private FileBrowserEntryScopeFilter m_entryScopeFilter = FileBrowserEntryScopeFilter.CurrentOnly;
    private float m_gridScale = ImGuiWidget.style.assetGridDefaultScale;
    private EditorRenameSession? m_presentedRenameSession;
    private bool m_openRenamePopup;
    private bool m_focusRename;
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
    internal FileBrowserPanel(AssetEditorModule assets)
    {
        m_assets = assets;
        m_data = new FileBrowserData(assets);
        m_navigation = new FileBrowserNavigation(assets);
        m_dragDrop = new FileBrowserDragDrop(assets);
        m_changeTracker = new FileBrowserChangeTracker(assets);
        m_tree = new FileBrowserTree(
            m_data,
            m_navigation,
            m_dragDrop,
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
        PushBrowserStyle();
        DrawBrowser(context);
        PrepareRenamePopup(context);
        DrawRenamePopup(context);
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
        return MathF.Max(ImGuiWidget.style.assetSplitterMinimumWidth, style.DockingSeparatorSize);
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
                m_treeWidth = Math.Clamp(m_treeWidth + delta.X, ImGuiWidget.style.assetTreeMinimumWidth, ImGuiWidget.style.assetTreeMaximumWidth);
                NativeImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
            }
        }

        Vector2 min = NativeImGui.GetItemRectMin();
        Vector2 max = NativeImGui.GetItemRectMax();
        Vector4 color = active ? EditorPalette.assetAccent : hovered ? EditorPalette.assetBorder : EditorPalette.assetBorderSoft;
        NativeImGui.AddRectFilled(NativeImGui.GetWindowDrawList(), min, max, NativeImGui.ColorConvertFloat4ToU32(color));
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
        DrawViewAndSearchBar(context);
    }

    private void DrawNavigationBar(EditorContext context)
    {
        string current = m_assets.browser.currentDirectory;

        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ImGuiWidget.style.breadcrumbFramePadding);
        bool canGoBack = m_navigation.canGoBack;
        PushButtonColors(canGoBack ? EditorPalette.assetAccent : EditorPalette.assetBorderSoft);

        NativeImGui.BeginDisabled(!canGoBack);
        if (NativeImGui.SmallButton($"{ImGuiIcon.AngleLeft}##Back"))
            m_navigation.GoBack(context);

        NativeImGui.EndDisabled();
        NativeImGui.PopStyleColor(3);

        NativeImGui.SameLine(0f, ImGuiWidget.style.assetToolbarTightSpacing);
        bool canGoForward = m_navigation.canGoForward;
        PushButtonColors(canGoForward ? EditorPalette.assetAccent : EditorPalette.assetBorderSoft);
        NativeImGui.BeginDisabled(!canGoForward);
        if (NativeImGui.SmallButton($"{ImGuiIcon.AngleRight}##Forward"))
            m_navigation.GoForward(context);

        NativeImGui.EndDisabled();
        NativeImGui.PopStyleColor(3);

        NativeImGui.SameLine(0f, ImGuiWidget.style.assetToolbarSectionSpacing);

        NativeImGui.PushStyleColor(ImGuiCol.Text, EditorPalette.assetText);
        NativeImGui.TextUnformatted(GetDirectoryLabel(current));
        NativeImGui.PopStyleColor();

        NativeImGui.PopStyleVar();
    }

    private void DrawViewAndSearchBar(EditorContext context)
    {
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ImGuiWidget.style.toolbarFramePadding);

        PushButtonColors(EditorPalette.assetAccent);
        if (NativeImGui.SmallButton($"{m_viewMode}##ViewMode"))
            m_viewMode = m_viewMode == ViewMode.List ? ViewMode.Grid : ViewMode.List;

        NativeImGui.SameLine(0f, ImGuiWidget.style.assetToolbarSpacing);

        if (NativeImGui.SmallButton("New Folder##CreateAssetFolder"))
        {
            _ = context.Execute(
                AssetActionIds.CreateFolder,
                typeof(AssetSurface.Browser),
                context.selection.selectedTarget);
        }
        NativeImGui.PopStyleColor(3);
        NativeImGui.SameLine(0f, ImGuiWidget.style.assetToolbarSpacing);

        DrawEntryFilterCombo();
        NativeImGui.SameLine(0f, ImGuiWidget.style.assetToolbarSpacing);

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
            DrawEntriesTable(context, entries, m_assets.browser.currentDirectory);

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
        m_gridScale = Math.Clamp(m_gridScale, ImGuiWidget.style.assetGridMinimumScale, ImGuiWidget.style.assetGridMaximumScale);
        DrawGridScaleTopSplitter();
        Vector2 cursor = NativeImGui.GetCursorPos();
        float labelOffsetY = MathF.Max(0f, (NativeImGui.GetFrameHeight() - NativeImGui.GetTextLineHeight()) * 0.5f);
        NativeImGui.SetCursorPosY(cursor.Y + labelOffsetY);
        NativeImGui.TextUnformatted("Scale");
        NativeImGui.SameLine();
        NativeImGui.SetCursorPosY(cursor.Y);
        NativeImGui.SetNextItemWidth(-1f);
        _ = NativeImGui.SliderFloat("##GridScale", ref m_gridScale, ImGuiWidget.style.assetGridMinimumScale, ImGuiWidget.style.assetGridMaximumScale, "%.1f");
        m_gridScale = Math.Clamp(m_gridScale, ImGuiWidget.style.assetGridMinimumScale, ImGuiWidget.style.assetGridMaximumScale);
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
            ImGuiWidget.style.borderSize);
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

        NativeImGui.TableSetupColumn(
            "Name",
            ImGuiTableColumnFlags.WidthFixed,
            ImGuiWidget.style.assetListNameColumnWidth);
        NativeImGui.TableSetupColumn(
            "Type",
            ImGuiTableColumnFlags.WidthFixed,
            ImGuiWidget.style.assetListTypeColumnWidth);
        NativeImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 1f);
        DrawHeaderRow();

        uint rowBg = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.collectionRow);
        uint rowAltBg = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.collectionRowAlternate);
        for (int i = 0; i < entries.Count; i++)
        {
            AssetFileEntry entry = entries[i];
            if (i > 0)
                NativeImGui.TableNextRow(ImGuiTableRowFlags.None, ImGuiWidget.style.assetListRowSpacing);
            NativeImGui.TableNextRow();
            NativeImGui.TableSetBgColor(
                ImGuiTableBgTarget.RowBg0,
                i % 2 == 0 ? rowBg : rowAltBg);

            DrawNameCell(context, entry);
            DrawTextCell(GetTypeText(entry), EditorPalette.assetText);
            DrawTextCell(GetSourceText(entry, currentDirectory), EditorPalette.assetText);
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
        bool selected = string.Equals(m_assets.browser.GetSelectedPath(context), entry.relativePath, StringComparison.Ordinal);

        NativeImGui.PushStyleColor(ImGuiCol.Header, EditorPalette.transparent);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderHovered, EditorPalette.transparent);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderActive, EditorPalette.transparent);
        Vector2 iconTextPos = NativeImGui.GetCursorScreenPos();
        bool activated = NativeImGui.Selectable(
            $"##entry_{entry.relativePath}",
            selected,
            ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick);
        bool itemHovered = NativeImGui.IsItemHovered();
        bool doubleClicked = itemHovered && NativeImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
        if (activated)
        {
            HandleEntryActivation(context, entry, doubleClicked);
        }

        bool itemActive = NativeImGui.IsItemActive();
        DrawEntryContextMenu(context, entry.relativePath);
        if (selected || itemHovered)
        {
            Vector4 highlight = itemActive
                ? EditorPalette.GetHovered(EditorPalette.assetAccent)
                : EditorPalette.assetAccent;
            NativeImGui.TableSetBgColor(
                ImGuiTableBgTarget.RowBg1,
                NativeImGui.ColorConvertFloat4ToU32(highlight));
        }
        m_dragDrop.DrawAssetSource(context, entry);

        NativeImGui.SameLine(iconTextPos.X - NativeImGui.GetWindowPos().X, 0f);
        ImGuiWidget.IconText(icon, name, false);

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
        bool selected = string.Equals(m_assets.browser.GetSelectedPath(context), entry.relativePath, StringComparison.Ordinal);
        Vector2 itemSize = new(cellSize - ImGuiWidget.style.assetGridCellPadding, cellSize - ImGuiWidget.style.assetGridCellPadding);

        NativeImGui.PushID(entry.relativePath);
        NativeImGui.PushStyleColor(ImGuiCol.Header, EditorPalette.transparent);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderHovered, EditorPalette.transparent);
        NativeImGui.PushStyleColor(ImGuiCol.HeaderActive, EditorPalette.transparent);
        bool activated = NativeImGui.Selectable(
            "##GridItem",
            selected,
            ImGuiSelectableFlags.AllowDoubleClick,
            itemSize);
        bool doubleClicked = NativeImGui.IsItemHovered() &&
                             NativeImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
        if (activated)
        {
            HandleEntryActivation(context, entry, doubleClicked);
        }

        DrawEntryContextMenu(context, entry.relativePath);
        m_dragDrop.DrawAssetSource(context, entry);
        DrawGridItemVisual(icon, name, selected, m_gridScale);
        NativeImGui.PopStyleColor(3);
        NativeImGui.PopID();
    }

    private void HandleEntryActivation(
        EditorContext context,
        AssetFileEntry entry,
        bool doubleClicked)
    {
        if (doubleClicked)
        {
            m_navigation.OpenEntry(context, entry, m_tree);
            return;
        }

        m_assets.browser.Select(context, entry.relativePath);
        m_tree.RequestRevealPath(entry.relativePath);
    }

    private void DrawEntryContextMenu(EditorContext context, string relativePath)
    {
        if (NativeImGui.IsItemClicked(ImGuiMouseButton.Right))
            m_assets.browser.Select(context, relativePath);
        _ = EditorMenuRenderer.ContextMenu(
            $"##asset_context_{relativePath}",
            new EditorMenuContext(
                context,
                typeof(AssetSurface.ContextMenu),
                new AssetSelectionTarget(relativePath)));
    }

    private void PrepareRenamePopup(EditorContext context)
    {
        EditorRenameSession? session = m_assets.rename;
        if (session?.target is not AssetSelectionTarget || ReferenceEquals(session, m_presentedRenameSession))
            return;
        m_presentedRenameSession = session;
        m_openRenamePopup = true;
        m_focusRename = true;
    }

    private void DrawRenamePopup(EditorContext context)
    {
        const string C_POPUP_ID = "Rename Asset##FileBrowserRename";
        EditorRenameSession? session = m_presentedRenameSession;
        if (session is null || session.isCompleted)
            return;
        if (m_openRenamePopup)
        {
            NativeImGui.OpenPopup(C_POPUP_ID, ImGuiPopupFlags.NoReopen);
            m_openRenamePopup = false;
        }
        if (!NativeImGui.BeginPopupModal(C_POPUP_ID, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (m_focusRename)
        {
            NativeImGui.SetKeyboardFocusHere();
            m_focusRename = false;
        }
        NativeImGui.SetNextItemWidth(ImGuiWidget.style.assetRenameWidth);
        string buffer = session.buffer;
        bool submitted = NativeImGui.InputText(
            "##AssetRenameInput",
            ref buffer,
            512,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        session.buffer = buffer;
        bool cancel = NativeImGui.IsKeyPressed(ImGuiKey.Escape);
        if (submitted || NativeImGui.Button("Rename"))
        {
            try
            {
                EditorValidationResult validation = session.Commit();
                if (validation.isValid)
                    NativeImGui.CloseCurrentPopup();
                else
                    Inno.Core.Logging.Log.Warn("Asset rename was rejected: {0}", validation.message);
            }
            catch (Exception exception)
            {
                Inno.Core.Logging.Log.Error(
                    "Failed to rename asset to '{0}': {1}",
                    session.buffer,
                    exception);
            }
        }
        NativeImGui.SameLine();
        if (cancel || NativeImGui.Button("Cancel"))
        {
            m_assets.CancelRename();
            NativeImGui.CloseCurrentPopup();
        }
        NativeImGui.EndPopup();
    }

    private static unsafe void DrawGridItemVisual(string icon, string name, bool selected, float scale)
    {
        bool hovered = NativeImGui.IsItemHovered();
        bool active = NativeImGui.IsItemActive();
        Vector2 min = NativeImGui.GetItemRectMin();
        Vector2 max = NativeImGui.GetItemRectMax();
        Vector2 size = max - min;

        Vector4 bg = selected ? EditorPalette.assetAccent : EditorPalette.collectionHeader;
        if (active)
            bg = EditorPalette.GetActive(bg);
        else if (hovered)
            bg = EditorPalette.GetHovered(bg);

        uint bgColor = NativeImGui.ColorConvertFloat4ToU32(bg);
        uint textColor = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.assetText);
        ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, bgColor, ImGuiWidget.style.assetFrameRounding);

        ImFontPtr font = NativeImGui.GetFont();
        float fontSize = NativeImGui.GetFontSize();
        float iconFontSize = fontSize * scale;
        Vector2 iconSize = NativeImGui.CalcTextSize(icon) * scale;
        string[] nameLines = FitTextToLines(
            name,
            MathF.Max(1f, size.X - ImGuiWidget.style.assetGridLabelHorizontalPadding),
            2);
        float lineHeight = NativeImGui.CalcTextSize("A").Y;
        float labelHeight = lineHeight * nameLines.Length;
        float labelY = max.Y - labelHeight - ImGuiWidget.style.assetGridLabelBottomPadding;
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
        return MathF.Max(
            ImGuiWidget.style.assetGridMinimumCellSize,
            fontSize * (m_gridScale + ImGuiWidget.style.assetGridScaleBias) +
            ImGuiWidget.style.assetGridFixedCellPadding);
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
        IReadOnlyList<(string Label, string Path)> parts = BuildBreadcrumbParts(m_assets.browser.currentDirectory);
        Vector2 framePadding = ImGuiWidget.style.breadcrumbFramePadding;
        float contentWidth = CalculateBreadcrumbContentWidth(parts, framePadding);
        NativeImGui.SetNextWindowContentSize(new Vector2(MathF.Max(contentWidth, NativeImGui.GetContentRegionAvail().X), 0f));
        NativeImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (NativeImGui.BeginChild("##BreadcrumbBar", new Vector2(0f, height), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            DrawBreadcrumbTopSeparator();
            NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, framePadding);
            PushButtonColors(EditorPalette.assetAccent);
            float contentHeight = contentWidth > NativeImGui.GetWindowSize().X ? ImGuiWidget.style.assetBreadcrumbHeight : height;
            NativeImGui.SetCursorPosY(MathF.Max(0f, (contentHeight - NativeImGui.GetFrameHeight()) * 0.5f));

            for (int i = 0; i < parts.Count; i++)
            {
                (string label, string path) = parts[i];
                if (i > 0)
                {
                    NativeImGui.SameLine(0f, ImGuiWidget.style.assetBreadcrumbSpacing);
                    NativeImGui.TextUnformatted(">");
                    NativeImGui.SameLine(0f, ImGuiWidget.style.assetBreadcrumbSpacing);
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
        float contentWidth = CalculateBreadcrumbContentWidth(
            parts,
            ImGuiWidget.style.breadcrumbFramePadding);
        return contentWidth > NativeImGui.GetContentRegionAvail().X
            ? ImGuiWidget.style.assetBreadcrumbHeight + NativeImGui.GetStyle().ScrollbarSize
            : ImGuiWidget.style.assetBreadcrumbHeight;
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
                width += separatorWidth + ImGuiWidget.style.assetBreadcrumbSpacing * 2f;

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
            ImGuiWidget.style.borderSize);
    }
    #endregion

}
