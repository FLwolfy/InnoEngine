using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Inno.Assets;
using Inno.Core.Logging;
using Inno.Core.Math;
using Inno.Editor.Core;
using Inno.Editor.GUI;
using Inno.ImGui;

using ImGuiNET;
using Inno.Assets.AssetType;
using Inno.Assets.Core;
using Inno.Platform;
using ImGuiNet = ImGuiNET.ImGui;

namespace Inno.Editor.Panel;

public sealed class FileBrowserPanel : EditorPanel
{
    /// <summary>
    /// Gets the panel title.
    /// </summary>
    public override string title => "File";

    private const float C_SPLITTER_WIDTH = 3f;
    private const float C_LEFT_MIN_WIDTH = 10f;
    private const float C_RIGHT_MIN_WIDTH = 20f;
    private float m_leftWidth = -1f;
    private float m_leftRatio = -1f;

    private const float C_GRID_ICON_SIZE = 54f;
    private const double C_SNAPSHOT_TTL_SECONDS = 0.5;
    private const float C_GRID_SCALE_MIN = 0.2f;
    private const float C_GRID_SCALE_MAX = 5.0f;
    private float m_gridScale = 1.0f;

    private readonly string m_rootPath;
    private string m_currentDir;
    private readonly string m_rootPathNative;
    private string m_currentDirNative;
    private string? m_selectedPath;
    
    private bool m_navPending;
    private string m_navTarget = "";
    private bool m_navPushHistory;

    private readonly HashSet<string> m_revealOpenPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool m_revealOpenPending;

    private readonly DirectorySnapshot m_snapshot = new();
    private DateTime m_snapshotTime = DateTime.MinValue;
    private readonly List<string> m_history = new();
    private int m_historyIndex = -1;
    private int m_assetAppliedVersion;

    private readonly DirectorySnapshot m_searchSnapshot = new();
    private DateTime m_searchSnapshotTime = DateTime.MinValue;
    private string m_search = "";
    private string m_searchLast = "";

    private ViewMode m_viewMode = ViewMode.Grid;
    private SortField m_sortField = SortField.Name;
    private bool m_sortAscending = true;

    private string? m_renameTargetPath;
    private string m_renameBuffer = "";
    private string? m_deleteTargetPath;
    private bool m_requestOpenRenamePopup;
    private bool m_requestOpenDeletePopup;

    private readonly Dictionary<Guid, string> m_relByGuid = new();

    private enum ViewMode { Grid, List }
    private enum SortField { Name, Type, Source }

    private struct Entry(string fullPath, string name, bool isDir, string type, string source, Guid? guid)
    {
        public readonly string fullPath = fullPath;
        public readonly string name = name;
        public readonly bool isDir = isDir;
        public readonly string type = type;
        public readonly string source = source;
        public readonly Guid? guid = guid;
    }

    private sealed class DirectorySnapshot
    {
        public readonly List<Entry> entries = new();
        public void Clear() => entries.Clear();
    }

    #region Lifecycle

    internal FileBrowserPanel()
    {
        m_rootPathNative = Path.GetFullPath(AssetManager.assetDirectory);
        m_currentDirNative = m_rootPathNative;

        m_rootPath = NormalizePath(m_rootPathNative);
        m_currentDir = m_rootPath;

        PushHistory(m_currentDir);

        m_assetAppliedVersion = AssetManager.assetDirectoryChangeVersion;
        RefreshSnapshot(force: true);
    }

    internal override void OnGUI()
    {
        ImGuiNet.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10));
        ImGuiNet.BeginChild("##FileBrowserRoot", new Vector2(0, 0));

        float statusH = ImGuiNet.GetFrameHeight();
        var avail = ImGuiNet.GetContentRegionAvail();
        float bodyH = Math.Max(0f, avail.Y - statusH - 6f);

        ImGuiNet.BeginChild("##FileBrowserBody", new Vector2(0, bodyH));
        
        CommitPendingNavigate();

        var region = ImGuiNet.GetContentRegionAvail();
        float totalW = Math.Max(0f, region.X);
        if (totalW > 0f)
        {
            if (m_leftWidth < 0f || m_leftRatio < 0f)
            {
                m_leftRatio = 0.5f;
                m_leftWidth = Math.Clamp(totalW * m_leftRatio, C_LEFT_MIN_WIDTH,
                    Math.Max(C_LEFT_MIN_WIDTH, totalW - C_RIGHT_MIN_WIDTH));
            }
            else
            {
                if (m_leftRatio < 0f)
                    m_leftRatio = Math.Clamp(m_leftWidth / totalW, 0f, 1f);
            }
        }

        {
            ImGuiNet.BeginChild("##Tree", new Vector2(m_leftWidth, 0), ImGuiChildFlags.None,
                ImGuiWindowFlags.HorizontalScrollbar);

            DrawDirectoryTree(m_rootPathNative, m_rootPath);
            ImGuiNet.EndChild();
        }

        if (m_revealOpenPending)
        {
            m_revealOpenPending = false;
            m_revealOpenPaths.Clear();
        }

        {
            ImGuiNet.SameLine();
            float maxLeft = Math.Max(C_LEFT_MIN_WIDTH, totalW - C_RIGHT_MIN_WIDTH);
            bool draggingSplitter = DrawSplitter(ref m_leftWidth, minLeft: C_LEFT_MIN_WIDTH, maxLeft: maxLeft);
            ImGuiNet.SameLine();

            if (totalW > 0f)
            {
                float maxLeft2 = Math.Max(C_LEFT_MIN_WIDTH, totalW - C_RIGHT_MIN_WIDTH);

                if (draggingSplitter)
                    m_leftRatio = Math.Clamp(m_leftWidth / totalW, 0f, 1f);
                else
                    m_leftWidth = Math.Clamp(m_leftRatio * totalW, C_LEFT_MIN_WIDTH, maxLeft2);
            }
        }

        {
            ImGuiNet.BeginChild("##Content", new Vector2(0, 0));
            DrawToolbar();
            DrawContent();
            ImGuiNet.EndChild();
        }

        ImGuiNet.EndChild();
        ImGuiNet.Separator();
        DrawStatusBarFinderPath(statusH);

        ImGuiNet.EndChild();
        ImGuiNet.PopStyleVar();

        DrawRenamePopup();
        DrawDeletePopup();
    }

    #endregion

    #region Toolbar

    private void DrawToolbar()
    {
        ImGuiNet.AlignTextToFramePadding();

        bool canBack = m_historyIndex > 0;
        bool canForward = m_historyIndex >= 0 && m_historyIndex < m_history.Count - 1;

        ImGuiNet.BeginDisabled(!canBack);
        if (ImGuiNet.Button("<##Back")) NavigateHistory(-1);
        ImGuiNet.EndDisabled();
        ImGuiNet.SameLine();

        ImGuiNet.BeginDisabled(!canForward);
        if (ImGuiNet.Button(">##Forward")) NavigateHistory(+1);
        ImGuiNet.EndDisabled();
        ImGuiNet.SameLine();

        ImGuiNet.TextUnformatted(GetCurrentFolderDisplayName());
    }

    #endregion

    #region Tree

    private void DrawDirectoryTree(string pathNative, string pathNormalized)
    {
        if (!Directory.Exists(pathNative))
            return;

        string displayName = IsSamePath(pathNormalized, m_rootPath) ? "Assets" : Path.GetFileName(pathNative);

        bool selected = IsSelected(pathNormalized);

        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth;
        if (selected) flags |= ImGuiTreeNodeFlags.Selected;

        bool shouldOpen = IsAncestorOrSelf(pathNormalized, m_currentDir);

        if (m_revealOpenPending && m_revealOpenPaths.Contains(pathNormalized))
            ImGuiNet.SetNextItemOpen(true, ImGuiCond.Always);
        else
            ImGuiNet.SetNextItemOpen(shouldOpen, ImGuiCond.Once);

        bool open = ImGuiNet.TreeNodeEx($"##tree_{pathNormalized}", flags);

        if (ImGuiNet.IsItemClicked(ImGuiMouseButton.Left))
            SelectFolder(pathNormalized);

        if (ImGuiNet.IsItemHovered() && ImGuiNet.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            RequestNavigate(pathNormalized, pushHistory: true);

        if (!IsSamePath(pathNormalized, m_rootPath))
        {
            EmitFolderDragSource(pathNormalized, displayName);
        }

        if (BeginMoveDropTarget(pathNormalized, out var srcRel))
        {
            string dstFolderRel = AssetManager.NormalizeRelativePath(AssetManager.ToRelativePathFromAssetDirectory(ToNativePath(pathNormalized)));
            TryMoveToFolder(srcRel, dstFolderRel);
        }

        bool insideDirectory = IsSamePath(pathNormalized, m_currentDir);
        if (insideDirectory)
        {
            ImGuiHost.UseFont(ImGuiFontStyle.BoldItalic);
            ImGuiNet.SameLine();
            EditorImGuiEx.DrawIconAndText(ImGuiIcon.Folder, displayName);
            EditorImGuiEx.UnderlineLastItem();
            ImGuiHost.UseFont(ImGuiFontStyle.Regular);
        }
        else
        {
            ImGuiNet.SameLine();
            EditorImGuiEx.DrawIconAndText(ImGuiIcon.Folder, displayName);
        }

        if (ImGuiNet.BeginPopupContextItem($"##tree_ctx_{pathNormalized}"))
        {
            DrawCommonContextItems(pathNormalized);
            ImGuiNet.EndPopup();
        }

        if (!open)
            return;

        try
        {
            foreach (var dir in Directory.GetDirectories(pathNative))
            {
                if (IsHidden(dir)) continue;
                DrawDirectoryTree(dir, NormalizePath(dir));
            }

            foreach (var file in Directory.GetFiles(pathNative))
            {
                if (IsHidden(file)) continue;
                if (IsEditorFilteredFile(file)) continue;
                DrawTreeFileItem(file);
            }
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }

        ImGuiNet.TreePop();
    }

    private void DrawTreeFileItem(string filePathNative)
    {
        string full = NormalizePath(filePathNative);
        string name = Path.GetFileName(full);

        var fi = new FileInfo(filePathNative);
        string type = ToType(fi.Extension);
        bool selected = IsSelected(full);

        var flags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanFullWidth;
        if (selected) flags |= ImGuiTreeNodeFlags.Selected;

        ImGuiNet.TreeNodeEx($"##tree_file_{full}", flags);

        if (ImGuiNet.IsItemClicked(ImGuiMouseButton.Left))
            SelectFile(full);

        if (ImGuiNet.IsItemHovered() && ImGuiNet.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            TryOpenFile(full);

        Guid? guid = TryGetGuidForFileByNativePath(filePathNative);
        var e = new Entry(
            fullPath: full,
            name: name,
            isDir: false,
            type: type,
            source: "~",
            guid: guid
        );

        CacheGuidMapping(e);

        EmitFileDragSource(e);

        ImGuiNet.SameLine();
        EditorImGuiEx.DrawIconAndText(FileIcon(type), name);

        if (ImGuiNet.BeginPopupContextItem($"##tree_file_ctx_{full}"))
        {
            DrawItemContextItems(e);
            ImGuiNet.EndPopup();
        }
    }

    #endregion

    #region Content

    private void DrawContent()
    {
        DrawViewTopBar();
        ImGuiNet.Separator();

        bool searching = !string.IsNullOrWhiteSpace(m_search);

        if (searching) RefreshSearchSnapshot(force: false);
        else RefreshSnapshot(force: false);

        if (ImGuiNet.BeginPopupContextWindow("##content_ctx",
                ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            DrawCommonContextItems(m_currentDir);
            ImGuiNet.EndPopup();
        }

        var src = searching ? m_searchSnapshot.entries : m_snapshot.entries;
        var entries = ApplyFilterAndSort(src);

        if (m_viewMode == ViewMode.Grid)
            DrawGridWithScaleBar(entries);
        else
            DrawListWithSortBar(entries);
    }

    private void DrawViewTopBar()
    {
        ImGuiNet.AlignTextToFramePadding();

        string viewLabel = m_viewMode == ViewMode.Grid ? "Grid" : "List";
        if (ImGuiNet.Button(viewLabel))
            m_viewMode = m_viewMode == ViewMode.Grid ? ViewMode.List : ViewMode.Grid;

        ImGuiNet.SameLine();

        float avail = ImGuiNet.GetContentRegionAvail().X;
        ImGuiNet.SetNextItemWidth(Math.Max(120f, avail));
        ImGuiNet.InputTextWithHint("##Search", "Search", ref m_search, 256);
    }

    private void DrawGrid(List<Entry> entries, float iconSize)
    {
        float availW = ImGuiNet.GetContentRegionAvail().X;
        float cellW = iconSize;

        int cols = Math.Max(1, (int)Math.Floor(availW / cellW));

        var flags = ImGuiTableFlags.SizingFixedFit |
                    ImGuiTableFlags.PadOuterX |
                    ImGuiTableFlags.NoBordersInBody |
                    ImGuiTableFlags.NoSavedSettings;

        if (!ImGuiNet.BeginTable("##grid_table", cols, flags))
            return;

        for (int c = 0; c < cols; c++)
            ImGuiNet.TableSetupColumn($"##gc{c}", ImGuiTableColumnFlags.WidthFixed, cellW);

        int col = 0;
        ImGuiNet.TableNextRow();

        foreach (var e in entries)
        {
            ImGuiNet.TableSetColumnIndex(col);
            DrawGridItem(e, iconSize, 15f);

            col++;
            if (col >= cols)
            {
                col = 0;
                ImGuiNet.TableNextRow();
            }
        }

        ImGuiNet.EndTable();
    }

    private void DrawGridItem(Entry e, float itemSize, float iconScale)
    {
        ImGuiNet.BeginGroup();
        ImGuiNet.PushID(e.guid.HasValue ? e.guid.Value.ToString() : e.fullPath);

        Vector2 p0 = ImGuiNet.GetCursorScreenPos();
        Vector2 size = new Vector2(itemSize, itemSize);

        ImGuiNet.InvisibleButton("##grid_item_btn", size);

        bool selected = IsSelected(e.fullPath);
        bool hovered = ImGuiNet.IsItemHovered();
        bool clicked = ImGuiNet.IsItemClicked(ImGuiMouseButton.Left);
        if (hovered || selected)
        {
            var col = ImGuiNet.GetColorU32(selected ? ImGuiCol.HeaderActive : ImGuiCol.HeaderHovered);
            uint a = (uint)(hovered && !selected ? 80 : 110);
            uint bg = (col & 0x00FFFFFFu) | (a << 24);
            float rounding = 10f;
            ImGuiNet.GetWindowDrawList().AddRectFilled(p0, p0 + size, bg, rounding);
        }

        if (clicked)
        {
            if (e.isDir) SelectFolder(e.fullPath);
            else SelectFile(e.fullPath);
        }

        if (hovered && ImGuiNet.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (e.isDir) RequestNavigate(e.fullPath, pushHistory: true);
            else TryOpenFile(e.fullPath);
        }

        if (ImGuiNet.BeginPopupContextItem("##item_ctx"))
        {
            DrawItemContextItems(e);
            ImGuiNet.EndPopup();
        }

        if (e.isDir)
            EmitFolderDragSource(e.fullPath, e.name);
        else
            EmitFileDragSource(e);

        if (e.isDir && BeginMoveDropTarget(e.fullPath, out var srcRel))
        {
            string dstFolderRel = AssetManager.NormalizeRelativePath(AssetManager.ToRelativePathFromAssetDirectory(ToNativePath(e.fullPath)));
            TryMoveToFolder(srcRel, dstFolderRel);
        }

        var icon = e.isDir ? ImGuiIcon.Folder : FileIcon(e.type);
        var currentFont = ImGuiHost.GetCurrentFont();
        ImGuiHost.UseFont(ImGuiFontStyle.Icon, itemSize * iconScale / ImGuiNet.GetFontSize());

        Vector2 winPos = ImGuiNet.GetWindowPos();
        Vector2 p0Local = p0 - winPos;
        Vector2 iconTextSize = ImGuiNet.CalcTextSize(icon);
        float iconX = MathF.Floor(p0Local.x + (itemSize - iconTextSize.x) * 0.5f);
        float iconY = MathF.Floor(p0Local.y + (itemSize - iconTextSize.y) * 0.5f);

        ImGuiNet.SetCursorPos(new Vector2(iconX, iconY));
        ImGuiNet.TextUnformatted(icon);
        ImGuiHost.UseFont(currentFont);
        ImGuiNet.SetCursorPos(new Vector2(p0Local.x, p0Local.y + itemSize));

        ImGuiNet.PushTextWrapPos(ImGuiNet.GetCursorPosX() + itemSize);
        var textWidth = ImGuiNet.CalcTextSize(e.name).X;
        var currentCursorX = ImGuiNet.GetCursorPosX();
        var offsetX = textWidth <= itemSize ? (itemSize - textWidth) / 2 : 0;
        ImGuiNet.SetCursorPosX(currentCursorX + offsetX);
        ImGuiNet.TextWrapped(e.name);
        ImGuiNet.PopTextWrapPos();

        ImGuiNet.PopID();
        ImGuiNet.EndGroup();
    }

    private void DrawGridWithScaleBar(List<Entry> entries)
    {
        float barH = ImGuiNet.GetFrameHeight() + ImGuiNet.GetStyle().ItemSpacing.Y * 2f;

        var avail = ImGuiNet.GetContentRegionAvail();
        float gridH = Math.Max(0f, avail.Y - barH);

        ImGuiNet.BeginChild("##GridArea", new Vector2(0, gridH));
        float iconSize = C_GRID_ICON_SIZE * m_gridScale;
        DrawGrid(entries, iconSize);
        ImGuiNet.EndChild();

        ImGuiNet.Separator();

        ImGuiNet.BeginChild("##GridScaleBar", new Vector2(0, 0), ImGuiChildFlags.None);
        DrawGridScaleSlider();
        ImGuiNet.EndChild();
    }

    private void DrawGridScaleSlider()
    {
        ImGuiNet.AlignTextToFramePadding();
        ImGuiNet.TextDisabled("Size");
        ImGuiNet.SameLine();

        float avail = ImGuiNet.GetContentRegionAvail().X;
        float sliderW = Math.Max(120f, avail);

        ImGuiNet.SetNextItemWidth(sliderW);

        if (ImGuiNet.SliderFloat("##grid_scale", ref m_gridScale, C_GRID_SCALE_MIN, C_GRID_SCALE_MAX, "%.2fx",
                ImGuiSliderFlags.NoInput))
        {
            m_gridScale = Math.Clamp(m_gridScale, C_GRID_SCALE_MIN, C_GRID_SCALE_MAX);
        }
    }

    private void DrawList(List<Entry> entries)
    {
        var tableFlags = ImGuiTableFlags.RowBg |
                         ImGuiTableFlags.BordersInnerV |
                         ImGuiTableFlags.Resizable |
                         ImGuiTableFlags.ScrollY |
                         ImGuiTableFlags.SizingFixedFit;

        var avail = ImGuiNet.GetContentRegionAvail();

        if (!ImGuiNet.BeginTable("##list_table", 3, tableFlags, new Vector2(0, avail.Y)))
            return;

        ImGuiNet.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed);
        ImGuiNet.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed);
        ImGuiNet.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch);

        ImGuiNet.TableHeadersRow();

        foreach (var e in entries)
        {
            float rowH = ImGuiNet.GetFrameHeight();

            ImGuiNet.TableNextRow(ImGuiTableRowFlags.None, rowH);
            ImGuiNet.TableSetColumnIndex(0);

            bool selected = IsSelected(e.fullPath);
            string uid = e.guid.HasValue ? e.guid.Value.ToString() : e.fullPath;
            string selId = $"##name_{uid}";

            if (ImGuiNet.Selectable(selId, selected, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, rowH)))
            {
                if (e.isDir) SelectFolder(e.fullPath);
                else SelectFile(e.fullPath);
            }

            if (ImGuiNet.IsItemHovered() && ImGuiNet.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                if (e.isDir) RequestNavigate(e.fullPath, pushHistory: true);
                else TryOpenFile(e.fullPath);
            }

            if (ImGuiNet.BeginPopupContextItem($"##list_ctx_{uid}"))
            {
                DrawItemContextItems(e);
                ImGuiNet.EndPopup();
            }

            if (e.isDir)
                EmitFolderDragSource(e.fullPath, e.name);
            else
                EmitFileDragSource(e);

            if (e.isDir && BeginMoveDropTarget(e.fullPath, out var srcRel))
            {
                string dstFolderRel = AssetManager.NormalizeRelativePath(AssetManager.ToRelativePathFromAssetDirectory(ToNativePath(e.fullPath)));
                TryMoveToFolder(srcRel, dstFolderRel);
            }

            ImGuiNet.SameLine();
            ImGuiNet.SetCursorPosY(ImGuiNet.GetCursorPosY() + ImGuiNet.GetStyle().FramePadding.Y);
            EditorImGuiEx.DrawIconAndText(e.isDir ? ImGuiIcon.Folder : FileIcon(e.type), e.name);

            ImGuiNet.TableSetColumnIndex(1);
            ImGuiNet.AlignTextToFramePadding();
            ImGuiNet.TextUnformatted(e.isDir ? "FOLDER" : e.type);

            ImGuiNet.TableSetColumnIndex(2);
            ImGuiNet.AlignTextToFramePadding();
            ImGuiNet.TextUnformatted(e.source);
        }

        ImGuiNet.EndTable();
    }

    private void DrawListWithSortBar(List<Entry> entries)
    {
        float barH = ImGuiNet.GetFrameHeight() + ImGuiNet.GetStyle().ItemSpacing.Y * 2f;
        var avail = ImGuiNet.GetContentRegionAvail();
        float listH = Math.Max(0f, avail.Y - barH);

        ImGuiNet.BeginChild("##ListArea", new Vector2(0, listH));
        DrawList(entries);
        ImGuiNet.EndChild();

        ImGuiNet.Separator();

        ImGuiNet.BeginChild("##ListSortBar", new Vector2(0, 0), ImGuiChildFlags.None);
        DrawListSortBar();
        ImGuiNet.EndChild();
    }

    private void DrawListSortBar()
    {
        ImGuiNet.AlignTextToFramePadding();

        ImGuiNet.TextDisabled("Sort");
        ImGuiNet.SameLine();

        if (ImGuiNet.Button(m_sortAscending ? "Asc##sort" : "Desc##sort"))
            m_sortAscending = !m_sortAscending;

        ImGuiNet.SameLine();

        float w = ImGuiNet.GetContentRegionAvail().X;
        ImGuiNet.SetNextItemWidth(Math.Max(80f, w));

        SortCombo("##sortField", ref m_sortField);
    }

    private static void SortCombo(string id, ref SortField field)
    {
        if (!ImGuiNet.BeginCombo(id, field.ToString()))
            return;

        foreach (SortField v in Enum.GetValues(typeof(SortField)))
        {
            bool selected = (v == field);
            if (ImGuiNet.Selectable(v.ToString(), selected))
                field = v;

            if (selected)
                ImGuiNet.SetItemDefaultFocus();
        }

        ImGuiNet.EndCombo();
    }

    #endregion

    #region Context Menus

    private void DrawItemContextItems(Entry e)
    {
        if (e.isDir)
        {
            if (ImGuiNet.MenuItem("Open"))
                RequestNavigate(e.fullPath, pushHistory: true);
        }

        if (ImGuiNet.MenuItem("Reveal in Explorer"))
            PlatformAPI.RevealInSystem(ToNativePath(e.fullPath));

        ImGuiNet.Separator();

        if (ImGuiNet.MenuItem("Rename"))
            BeginRename(e.fullPath);

        if (ImGuiNet.MenuItem("Delete"))
            BeginDelete(e.fullPath);
    }

    private void DrawCommonContextItems(string targetFolderNormalized)
    {
        if (ImGuiNet.MenuItem("New Folder"))
            CreateNewFolder(targetFolderNormalized);

        if (ImGuiNet.MenuItem("Reveal in Finder/Explorer"))
            PlatformAPI.RevealInSystem(ToNativePath(targetFolderNormalized));
    }

    #endregion

    #region Navigation
    
    private void RequestNavigate(string dirNormalized, bool pushHistory)
    {
        dirNormalized = NormalizePath(dirNormalized);
        if (string.IsNullOrWhiteSpace(dirNormalized))
            return;

        m_navPending = true;
        m_navTarget = dirNormalized;
        m_navPushHistory = pushHistory;
    }
    
    private void CommitPendingNavigate()
    {
        if (!m_navPending)
            return;

        m_navPending = false;
        
        if (IsSamePath(m_navTarget, m_currentDir))
            return;

        string dirNative = ToNativePath(m_navTarget);
        if (!Directory.Exists(dirNative))
            return;

        m_currentDir = m_navTarget;
        m_currentDirNative = dirNative;

        ClearSelection();
        RevealFolderInTree(m_navTarget);

        if (m_navPushHistory)
            PushHistory(m_navTarget);

        RefreshSnapshot(force: true);
    }

    private void RevealFolderInTree(string folderNormalized)
    {
        m_revealOpenPaths.Clear();
        m_revealOpenPending = false;

        if (string.IsNullOrWhiteSpace(folderNormalized))
            return;

        folderNormalized = NormalizePath(folderNormalized);

        if (!IsAncestorOrSelf(m_rootPath, folderNormalized))
            return;

        string running = m_rootPath.TrimEnd('/');
        var parts = SplitPathRelativeToRoot(folderNormalized, m_rootPath);

        m_revealOpenPaths.Add(m_rootPath);

        foreach (var part in parts)
        {
            running = NormalizePath(Path.Combine(running, part));
            m_revealOpenPaths.Add(running);
        }

        m_revealOpenPending = true;
    }


    private void PushHistory(string dir)
    {
        if (m_historyIndex >= 0 && m_historyIndex < m_history.Count - 1)
            m_history.RemoveRange(m_historyIndex + 1, m_history.Count - (m_historyIndex + 1));

        m_history.Add(dir);
        m_historyIndex = m_history.Count - 1;
    }

    private void NavigateHistory(int delta)
    {
        int next = m_historyIndex + delta;
        if (next < 0 || next >= m_history.Count)
            return;

        m_historyIndex = next;
        RequestNavigate(m_history[m_historyIndex], pushHistory: false);
    }

    #endregion

    #region Snapshot

    private void RefreshSnapshot(bool force)
    {
        if (!force)
        {
            int ver = AssetManager.assetDirectoryChangeVersion;
            bool changed = ver != m_assetAppliedVersion;

            if (changed)
            {
                m_assetAppliedVersion = ver;
            }
            else
            {
                var now = DateTime.UtcNow;
                if ((now - m_snapshotTime).TotalSeconds < C_SNAPSHOT_TTL_SECONDS)
                    return;
            }
        }

        m_snapshotTime = DateTime.UtcNow;
        m_snapshot.Clear();
        m_relByGuid.Clear();

        try
        {
            if (!Directory.Exists(m_currentDirNative))
                return;

            foreach (var d in Directory.GetDirectories(m_currentDirNative))
            {
                if (IsHidden(d)) continue;

                m_snapshot.entries.Add(new Entry(
                    fullPath: NormalizePath(d),
                    name: Path.GetFileName(d),
                    isDir: true,
                    type: "Folder",
                    source: "~",
                    guid: null
                ));
            }

            foreach (var f in Directory.GetFiles(m_currentDirNative))
            {
                if (IsHidden(f)) continue;
                if (IsEditorFilteredFile(f)) continue;

                var fi = new FileInfo(f);

                string rel = AssetManager.NormalizeRelativePath(AssetManager.ToRelativePathFromAssetDirectory(f));
                Guid? g = TryGetGuidByRel(rel);

                var e = new Entry(
                    fullPath: NormalizePath(f),
                    name: fi.Name,
                    isDir: false,
                    type: ToType(fi.Extension),
                    source: "~",
                    guid: g
                );

                CacheGuidMapping(e, rel);
                m_snapshot.entries.Add(e);
            }
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    private void RefreshSearchSnapshot(bool force)
    {
        if (!force)
        {
            bool queryChanged = !string.Equals(m_search, m_searchLast, StringComparison.Ordinal);
            int ver = AssetManager.assetDirectoryChangeVersion;
            bool changed = ver != m_assetAppliedVersion;

            if (!queryChanged)
            {
                if (!changed)
                {
                    var now = DateTime.UtcNow;
                    if ((now - m_searchSnapshotTime).TotalSeconds < C_SNAPSHOT_TTL_SECONDS)
                        return;
                }
            }
        }

        m_searchLast = m_search;
        m_searchSnapshotTime = DateTime.UtcNow;

        m_assetAppliedVersion = AssetManager.assetDirectoryChangeVersion;

        m_searchSnapshot.Clear();
        m_relByGuid.Clear();

        try
        {
            if (string.IsNullOrWhiteSpace(m_search))
                return;

            if (!Directory.Exists(m_currentDirNative))
                return;

            foreach (var d in Directory.EnumerateDirectories(m_currentDirNative, "*", SearchOption.AllDirectories))
            {
                if (IsHidden(d)) continue;

                string rel = GetRelativeDisplay(m_currentDirNative, d);
                string name = Path.GetFileName(d);
                string src = BuildSourceFromRelative(rel);

                m_searchSnapshot.entries.Add(new Entry(
                    fullPath: NormalizePath(d),
                    name: string.IsNullOrWhiteSpace(name) ? rel : name,
                    isDir: true,
                    type: "Folder",
                    source: src,
                    guid: null
                ));
            }

            foreach (var f in Directory.EnumerateFiles(m_currentDirNative, "*", SearchOption.AllDirectories))
            {
                if (IsHidden(f)) continue;
                if (IsEditorFilteredFile(f)) continue;

                var fi = new FileInfo(f);

                string relDisplay = GetRelativeDisplay(m_currentDirNative, f);
                string src = BuildSourceFromRelative(relDisplay);

                string rel = AssetManager.NormalizeRelativePath(AssetManager.ToRelativePathFromAssetDirectory(f));
                Guid? g = TryGetGuidByRel(rel);

                var e = new Entry(
                    fullPath: NormalizePath(f),
                    name: fi.Name,
                    isDir: false,
                    type: ToType(fi.Extension),
                    source: src,
                    guid: g
                );

                CacheGuidMapping(e, rel);
                m_searchSnapshot.entries.Add(e);
            }
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    private List<Entry> ApplyFilterAndSort(List<Entry> src)
    {
        IEnumerable<Entry> q = src;

        if (!string.IsNullOrWhiteSpace(m_search))
        {
            string s = m_search.Trim();
            q = q.Where(e =>
                e.name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                e.source.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        q = q.OrderByDescending(e => e.isDir);

        Func<Entry, object> key = m_sortField switch
        {
            SortField.Name => e => e.name,
            SortField.Type => e => e.type,
            SortField.Source => e => e.source,
            _ => e => e.name
        };

        q = m_sortAscending ? q.OrderBy(key) : q.OrderByDescending(key);
        return q.ToList();
    }

    private static Guid? TryGetGuidByRel(string rel)
    {
        try
        {
            return AssetManager.GetGuid(rel);
        }
        catch
        {
            return null;
        }
    }

    private Guid? TryGetGuidForFileByNativePath(string nativePath)
    {
        try
        {
            string rel = AssetManager.NormalizeRelativePath(AssetManager.ToRelativePathFromAssetDirectory(nativePath));
            if (string.IsNullOrWhiteSpace(rel))
                return null;
            return TryGetGuidByRel(rel);
        }
        catch
        {
            return null;
        }
    }

    private void CacheGuidMapping(in Entry e, string? relOverride = null)
    {
        if (!e.guid.HasValue)
            return;

        string rel;
        if (!string.IsNullOrWhiteSpace(relOverride))
        {
            rel = relOverride;
        }
        else
        {
            try
            {
                rel = AssetManager.NormalizeRelativePath(AssetManager.ToRelativePathFromAssetDirectory(ToNativePath(e.fullPath)));
            }
            catch
            {
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(rel))
            return;

        m_relByGuid[e.guid.Value] = AssetManager.NormalizeRelativePath(rel);
    }

    #endregion

    #region File Operations

    private void BeginRename(string pathNormalized)
    {
        m_renameTargetPath = pathNormalized;
        m_renameBuffer = Path.GetFileName(pathNormalized);
        m_requestOpenRenamePopup = true;
    }

    private void DrawRenamePopup()
    {
        if (m_requestOpenRenamePopup)
        {
            ImGuiNet.OpenPopup("Rename##popup");
            m_requestOpenRenamePopup = false;
        }

        if (!ImGuiNet.BeginPopupModal("Rename##popup", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGuiNet.TextUnformatted("New name:");
        ImGuiNet.SetNextItemWidth(360f);
        ImGuiNet.InputText("##rename", ref m_renameBuffer, 256);

        ImGuiNet.Spacing();

        bool ok = ImGuiNet.Button("OK", new Vector2(120, 0));
        ImGuiNet.SameLine();
        bool cancel = ImGuiNet.Button("Cancel", new Vector2(120, 0));

        if (ok && m_renameTargetPath != null)
        {
            TryRename(m_renameTargetPath, m_renameBuffer);
            m_renameTargetPath = null;
            ImGuiNet.CloseCurrentPopup();
        }
        else if (cancel)
        {
            m_renameTargetPath = null;
            ImGuiNet.CloseCurrentPopup();
        }

        ImGuiNet.EndPopup();
    }

    private void TryRename(string oldPathNormalized, string newName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newName))
                return;

            string dirNorm = NormalizePath(Path.GetDirectoryName(oldPathNormalized) ?? m_currentDir);
            string newPathNorm = NormalizePath(Path.Combine(dirNorm, newName));

            if (IsSamePath(oldPathNormalized, newPathNorm))
                return;

            string oldRel = AssetManager.NormalizeRelativePath(AssetManager.ToRelativePathFromAssetDirectory(ToNativePath(oldPathNormalized)));
            string newRel = AssetManager.NormalizeRelativePath(AssetManager.ToRelativePathFromAssetDirectory(ToNativePath(newPathNorm)));

            if (!AssetManager.RenamePath(oldRel, newRel))
                return;

            if (IsSelected(oldPathNormalized))
                SelectPath(newPathNorm);

            RefreshSnapshot(force: true);
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    private void BeginDelete(string pathNormalized)
    {
        m_deleteTargetPath = pathNormalized;
        m_requestOpenDeletePopup = true;
    }

    private void DrawDeletePopup()
    {
        if (m_requestOpenDeletePopup)
        {
            ImGuiNet.OpenPopup("Delete##popup");
            m_requestOpenDeletePopup = false;
        }

        if (!ImGuiNet.BeginPopupModal("Delete##popup", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        string name = m_deleteTargetPath != null ? Path.GetFileName(m_deleteTargetPath) : "";
        ImGuiNet.TextUnformatted($"Delete '{name}' ?");
        ImGuiNet.TextDisabled("This action cannot be undone.");

        ImGuiNet.Spacing();

        bool del = ImGuiNet.Button("Delete", new Vector2(120, 0));
        ImGuiNet.SameLine();
        bool cancel = ImGuiNet.Button("Cancel", new Vector2(120, 0));

        if (del && m_deleteTargetPath != null)
        {
            TryDelete(m_deleteTargetPath);
            m_deleteTargetPath = null;
            ImGuiNet.CloseCurrentPopup();
        }
        else if (cancel)
        {
            m_deleteTargetPath = null;
            ImGuiNet.CloseCurrentPopup();
        }

        ImGuiNet.EndPopup();
    }

    private void TryDelete(string pathNormalized)
    {
        try
        {
            string rel = AssetManager.NormalizeRelativePath(AssetManager.ToRelativePathFromAssetDirectory(ToNativePath(pathNormalized)));

            if (!AssetManager.DeletePath(rel))
                return;

            if (IsSelected(pathNormalized))
                ClearSelection();

            RefreshSnapshot(force: true);
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    private void CreateNewFolder(string parentFolderNormalized)
    {
        try
        {
            string parentNative = ToNativePath(parentFolderNormalized);
            if (!Directory.Exists(parentNative))
                return;

            string baseName = "New Folder";
            string candidateNative = Path.Combine(parentNative, baseName);
            int i = 1;
            while (Directory.Exists(candidateNative))
            {
                candidateNative = Path.Combine(parentNative, $"{baseName} ({i})");
                i++;
            }

            string rel = AssetManager.NormalizeRelativePath(AssetManager.ToRelativePathFromAssetDirectory(candidateNative));
            if (!AssetManager.CreateFolder(rel))
                return;

            string candidateNorm = NormalizePath(candidateNative);
            RefreshSnapshot(force: true);

            SelectFolder(candidateNorm);
            BeginRename(candidateNorm);
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    private void TryMoveToFolder(string srcRel, string dstFolderRel)
    {
        try
        {
            srcRel = AssetManager.NormalizeRelativePath(srcRel);
            dstFolderRel = AssetManager.NormalizeRelativePath(dstFolderRel);

            if (!AssetManager.MovePath(srcRel, dstFolderRel))
                return;

            RefreshSnapshot(force: true);
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    #endregion

    #region DragDrop

    private void EmitFileDragSource(in Entry e)
    {
        if (e.isDir)
            return;

        if (!e.guid.HasValue)
            return;

        if (!ImGuiNet.BeginDragDropSource())
            return;

        var guid = e.guid.Value;
        ImGuiHost.SetDragPayload(EditorPayloadType.ASSET_REF_PAYLOAD, AssetManager.Get<InnoAsset>(guid));
        ImGuiNet.TextUnformatted(e.name);

        ImGuiNet.EndDragDropSource();
    }

    private void EmitFolderDragSource(string folderNormalized, string displayName)
    {
        if (!ImGuiNet.BeginDragDropSource())
            return;

        try
        {
            string rel = AssetManager.NormalizeRelativePath(AssetManager.ToRelativePathFromAssetDirectory(ToNativePath(folderNormalized)));
            rel = AssetManager.NormalizeRelativePath(rel);

            if (!string.IsNullOrWhiteSpace(rel))
                ImGuiHost.SetDragPayload(EditorPayloadType.PATH_PAYLOAD, rel);
        }
        catch
        {
            // Do nothing
        }

        ImGuiNet.TextUnformatted(displayName);
        ImGuiNet.EndDragDropSource();
    }

    private bool BeginMoveDropTarget(string fullPathNormalizedFolder, out string srcRel)
    {
        srcRel = "";

        if (!ImGuiNet.BeginDragDropTarget())
            return false;
        
        try
        {
            if (!Directory.Exists(ToNativePath(fullPathNormalizedFolder)))
                return false;

            if (ImGuiHost.TryAcceptDragPayload(EditorPayloadType.ASSET_REF_PAYLOAD, out AssetRef<InnoAsset> assetRef))
            {
                if (!assetRef.isValid)
                    return false;
                
                if (!m_relByGuid.TryGetValue(assetRef.guid, out var rel) || string.IsNullOrWhiteSpace(rel))
                    return false;

                srcRel = AssetManager.NormalizeRelativePath(rel);
                return true;
            }
            
            if (ImGuiHost.TryAcceptDragPayload(EditorPayloadType.PATH_PAYLOAD, out string pathPayload))
            {
                if (string.IsNullOrWhiteSpace(pathPayload))
                    return false;

                srcRel = AssetManager.NormalizeRelativePath(pathPayload);
                return true;
            }

            return false;
        }
        finally
        {
            ImGuiNet.EndDragDropTarget();
        }
    }

    #endregion

    #region Splitter

    private static bool DrawSplitter(ref float leftWidth, float minLeft, float maxLeft)
    {
        float height = ImGuiNet.GetContentRegionAvail().Y;

        ImGuiNet.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGuiNet.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, 0));
        ImGuiNet.Button("##Splitter", new Vector2(C_SPLITTER_WIDTH, height));
        ImGuiNet.PopStyleVar(2);

        bool active = ImGuiNet.IsItemActive();
        if (active)
        {
            float delta = ImGuiNet.GetIO().MouseDelta.X;
            leftWidth = Math.Clamp(leftWidth + delta, minLeft, maxLeft);
        }

        if (ImGuiNet.IsItemHovered())
            ImGuiNet.SetMouseCursor(ImGuiMouseCursor.ResizeEW);

        return active;
    }

    #endregion

    #region StatusBar

    private void DrawStatusBarFinderPath(float height)
    {
        ImGuiNet.BeginChild("##FileBrowserStatus", new Vector2(0, height), ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar);

        if (ImGuiNet.SmallButton("Assets##sb_root"))
            RequestNavigate(m_rootPath, pushHistory: true);

        string running = m_rootPath;
        var parts = SplitPathRelativeToRoot(m_currentDir, m_rootPath);

        for (int i = 0; i < parts.Count; i++)
        {
            ImGuiNet.SameLine();
            ImGuiNet.TextDisabled(">");
            ImGuiNet.SameLine();

            running = NormalizePath(Path.Combine(running, parts[i]));
            if (ImGuiNet.SmallButton($"{parts[i]}##sb_{i}"))
                RequestNavigate(running, pushHistory: true);
        }

        ImGuiNet.EndChild();
    }

    #endregion

    #region Selection

    private void SelectPath(string? pathNormalized)
    {
        string? n = string.IsNullOrWhiteSpace(pathNormalized) ? null : NormalizePath(pathNormalized);
        if (IsSamePath(n, m_selectedPath))
            return;

        m_selectedPath = n;
        BuildRevealOpenPathsForSelection(n);
    }

    private void BuildRevealOpenPathsForSelection(string? selectedNormalized)
    {
        m_revealOpenPaths.Clear();
        m_revealOpenPending = false;

        if (string.IsNullOrWhiteSpace(selectedNormalized))
            return;

        string targetFolderNorm = GetFolderNormalizedForPath(selectedNormalized);

        if (!IsAncestorOrSelf(m_rootPath, targetFolderNorm))
            return;

        string running = m_rootPath.TrimEnd('/');
        var parts = SplitPathRelativeToRoot(targetFolderNorm, m_rootPath);

        m_revealOpenPaths.Add(m_rootPath);

        foreach (var part in parts)
        {
            running = NormalizePath(Path.Combine(running, part));
            m_revealOpenPaths.Add(running);
        }

        m_revealOpenPending = true;
    }

    private string GetFolderNormalizedForPath(string pathNormalized)
    {
        try
        {
            string native = ToNativePath(pathNormalized);

            if (Directory.Exists(native))
                return NormalizePath(native);

            string? parent = Path.GetDirectoryName(native);
            if (string.IsNullOrWhiteSpace(parent))
                return pathNormalized;

            return NormalizePath(parent);
        }
        catch
        {
            return pathNormalized;
        }
    }

    private void ClearSelection()
    {
        m_selectedPath = null;
        m_revealOpenPending = false;
        m_revealOpenPaths.Clear();
    }

    private void SelectFile(string filePathNormalized) => SelectPath(filePathNormalized);
    private void SelectFolder(string folderPathNormalized) => SelectPath(folderPathNormalized);

    private bool IsSelected(string? pathNormalized)
    {
        if (string.IsNullOrWhiteSpace(pathNormalized) || string.IsNullOrWhiteSpace(m_selectedPath))
            return false;

        return IsSamePath(pathNormalized, m_selectedPath);
    }

    #endregion

    #region Utility

    private static string NormalizePath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        return Path.GetFullPath(p).Replace('\\', '/');
    }

    private string ToNativePath(string normalized)
    {
        string n = NormalizePath(normalized).TrimEnd('/');
        string rootN = m_rootPath.TrimEnd('/');

        if (IsSamePath(n, rootN))
            return m_rootPathNative;

        if (!n.StartsWith(rootN, StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(normalized);

        string rel = n.Substring(rootN.Length).TrimStart('/');
        return Path.Combine(m_rootPathNative, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool IsSamePath(string? a, string? b)
    {
        if (a == null || b == null) return false;

        return string.Equals(
            NormalizePath(a).TrimEnd('/'),
            NormalizePath(b).TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool IsAncestorOrSelf(string ancestor, string path)
    {
        ancestor = NormalizePath(ancestor).TrimEnd('/') + "/";
        path = NormalizePath(path).TrimEnd('/') + "/";
        return path.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHidden(string path)
    {
        string name = Path.GetFileName(path);
        if (name.StartsWith(".", StringComparison.Ordinal)) return true;

        try
        {
            var attr = File.GetAttributes(path);
            return (attr & FileAttributes.Hidden) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsEditorFilteredFile(string file)
        => file.EndsWith(AssetManager.C_ASSET_POSTFIX, StringComparison.OrdinalIgnoreCase);

    private static List<string> SplitPathRelativeToRoot(string fullPath, string rootPath)
    {
        fullPath = NormalizePath(fullPath).TrimEnd('/');
        rootPath = NormalizePath(rootPath).TrimEnd('/');

        if (IsSamePath(fullPath, rootPath))
            return new List<string>();

        if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            return new List<string>();

        string rel = fullPath.Substring(rootPath.Length).TrimStart('/');
        return rel.Length == 0
            ? new List<string>()
            : rel.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string GetRelativeDisplay(string baseDirNative, string fullNative)
    {
        try
        {
            return Path.GetRelativePath(baseDirNative, fullNative).Replace('\\', '/');
        }
        catch
        {
            return Path.GetFileName(fullNative);
        }
    }

    private static string ToType(string? ext)
    {
        string e = ext ?? "";
        if (e.StartsWith(".")) e = e.Substring(1);
        if (string.IsNullOrWhiteSpace(e)) e = "File";
        return e.ToUpperInvariant();
    }

    private static string FileIcon(string type)
    {
        return type switch
        {
            "PNG" => ImGuiIcon.Image,
            "SCENE" => ImGuiIcon.ObjectGroup,
            "OBJ" => ImGuiIcon.Shapes,
            _ => ImGuiIcon.File
        };
    }

    private static void TryOpenFile(string fullPathNormalized)
    {
        var ext = Path.GetExtension(fullPathNormalized).ToUpperInvariant();
        switch (ext)
        {
            case ".SCENE":
            {
                string fullNative = fullPathNormalized.Replace('/', Path.DirectorySeparatorChar);
                string rel = GetRelativeDisplay(AssetManager.assetDirectory, fullNative);
                EditorSceneAssetIO.OpenScene(rel);
                break;
            }
        }
    }

    private string GetCurrentFolderDisplayName()
    {
        if (IsSamePath(m_currentDir, m_rootPath))
            return "Assets";

        string name = Path.GetFileName(m_currentDirNative.TrimEnd(Path.DirectorySeparatorChar, '/', '\\'));
        return string.IsNullOrWhiteSpace(name) ? "Assets" : name;
    }

    private static string BuildSourceFromRelative(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel))
            return "~";

        rel = rel.Replace('\\', '/').Trim('/');
        if (rel.Length == 0)
            return "~";

        int lastSlash = rel.LastIndexOf('/');
        if (lastSlash < 0)
            return "~";

        string parent = rel.Substring(0, lastSlash).Trim('/');
        if (string.IsNullOrWhiteSpace(parent))
            return "~";

        return "~/" + parent;
    }

    #endregion
}
