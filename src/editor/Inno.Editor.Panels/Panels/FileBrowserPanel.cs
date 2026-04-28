using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

using Inno.Assets;
using Inno.Assets.IO;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

/// <summary>
/// Unified file browser panel with tree + content view.
/// </summary>
public sealed class FileBrowserPanel : EditorPanel
{
    private const float C_TREE_DEFAULT_WIDTH = 280f;
    private const float C_GRID_CELL_WIDTH = 128f;

    private ViewMode m_viewMode = ViewMode.Grid;

    private enum ViewMode
    {
        Grid,
        List
    }

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
        DrawToolbar(context);
        NativeImGui.Separator();
        DrawBody(context);
        NativeImGui.Separator();
        DrawStatusBar(context);
    }

    private void DrawToolbar(EditorContext context)
    {
        string current = context.selection.currentDirectory;
        bool isRoot = string.IsNullOrEmpty(current);

        NativeImGui.BeginDisabled(isRoot);
        if (NativeImGui.Button($"{ImGuiIcon.ArrowUp} Up"))
        {
            string parent = Path.GetDirectoryName(current)?.Replace('\\', '/') ?? string.Empty;
            context.selection.SetCurrentDirectory(parent);
            context.selection.SetSelectedPath(parent);
        }

        NativeImGui.EndDisabled();
        NativeImGui.SameLine();

        bool grid = m_viewMode == ViewMode.Grid;
        if (NativeImGui.RadioButton("Grid", grid))
            m_viewMode = ViewMode.Grid;

        NativeImGui.SameLine();
        bool list = m_viewMode == ViewMode.List;
        if (NativeImGui.RadioButton("List", list))
            m_viewMode = ViewMode.List;

        NativeImGui.SameLine();
        NativeImGui.TextUnformatted(GetPathText(current));
    }

    private void DrawBody(EditorContext context)
    {
        Vector2 bodySize = new(0f, -NativeImGui.GetFrameHeightWithSpacing());
        if (!NativeImGui.BeginChild("##FileBrowserBody", bodySize))
        {
            NativeImGui.EndChild();
            return;
        }

        ImGuiTableFlags flags = ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp;
        if (NativeImGui.BeginTable("##FileBrowserSplit", 2, flags))
        {
            NativeImGui.TableSetupColumn("Tree", ImGuiTableColumnFlags.WidthFixed, C_TREE_DEFAULT_WIDTH);
            NativeImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch);

            NativeImGui.TableNextColumn();
            DrawTreePane(context);

            NativeImGui.TableNextColumn();
            DrawContentPane(context);

            NativeImGui.EndTable();
        }

        NativeImGui.EndChild();
    }

    private static void DrawTreePane(EditorContext context)
    {
        if (!NativeImGui.BeginChild("##TreePane", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            NativeImGui.EndChild();
            return;
        }

        DrawDirectoryNode(context, string.Empty, "Assets");
        NativeImGui.EndChild();
    }

    private static void DrawDirectoryNode(EditorContext context, string relativePath, string label)
    {
        IReadOnlyList<AssetFileEntry> children = AssetManager.GetFileSystemChildren(relativePath);
        List<AssetFileEntry> directories = [];
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].isDirectory)
                directories.Add(children[i]);
        }

        directories.Sort(static (a, b) => string.CompareOrdinal(a.relativePath, b.relativePath));

        bool selected = string.Equals(context.selection.currentDirectory, relativePath, StringComparison.Ordinal);
        bool isLeaf = directories.Count == 0;
        bool isRoot = relativePath.Length == 0;
        bool isAncestor = IsAncestorOrSelf(relativePath, context.selection.currentDirectory);

        if (!isRoot && isAncestor)
            NativeImGui.SetNextItemOpen(true, ImGuiCond.Once);

        bool open = ImGuiWidget.TreeNodeIcon(
            id: isRoot ? "root" : relativePath,
            icon: ImGuiIcon.Folder,
            label: label,
            selected: selected,
            isLeaf: isLeaf,
            defaultOpen: isRoot,
            drawLines: true);

        if (NativeImGui.IsItemClicked())
        {
            context.selection.SetCurrentDirectory(relativePath);
            context.selection.SetSelectedPath(relativePath);
        }

        if (!open || isLeaf)
            return;

        for (int i = 0; i < directories.Count; i++)
        {
            AssetFileEntry child = directories[i];
            string childName = Path.GetFileName(child.relativePath);
            DrawDirectoryNode(context, child.relativePath, childName);
        }

        NativeImGui.TreePop();
    }

    private void DrawContentPane(EditorContext context)
    {
        if (!NativeImGui.BeginChild("##ContentPane", Vector2.Zero))
        {
            NativeImGui.EndChild();
            return;
        }

        IReadOnlyList<AssetFileEntry> entries = AssetManager.GetFileSystemChildren(context.selection.currentDirectory);
        if (entries.Count == 0)
        {
            ImGuiWidget.Hint("Folder is empty.");
            NativeImGui.EndChild();
            return;
        }

        List<AssetFileEntry> sorted = SortEntries(entries);
        if (m_viewMode == ViewMode.Grid)
            DrawGrid(context, sorted);
        else
            DrawList(context, sorted);

        NativeImGui.EndChild();
    }

    private static List<AssetFileEntry> SortEntries(IReadOnlyList<AssetFileEntry> entries)
    {
        List<AssetFileEntry> sorted = new(entries.Count);
        for (int i = 0; i < entries.Count; i++)
            sorted.Add(entries[i]);

        sorted.Sort(static (a, b) =>
        {
            if (a.isDirectory != b.isDirectory)
                return a.isDirectory ? -1 : 1;
            return string.CompareOrdinal(a.relativePath, b.relativePath);
        });

        return sorted;
    }

    private void DrawGrid(EditorContext context, IReadOnlyList<AssetFileEntry> entries)
    {
        float available = MathF.Max(1f, NativeImGui.GetContentRegionAvail().X);
        int columns = Math.Max(1, (int)(available / C_GRID_CELL_WIDTH));

        if (!NativeImGui.BeginTable("##FileBrowserGrid", columns, ImGuiTableFlags.SizingStretchSame))
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
        string icon = entry.isDirectory ? ImGuiIcon.Folder : ImGuiIcon.File;
        string name = Path.GetFileName(entry.relativePath);
        bool selected = string.Equals(context.selection.selectedPath, entry.relativePath, StringComparison.Ordinal);

        if (NativeImGui.Selectable($"{icon} {name}##grid_{entry.relativePath}", selected))
            context.selection.SetSelectedPath(entry.relativePath);

        if (entry.isDirectory
            && NativeImGui.IsItemHovered()
            && NativeImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            context.selection.SetCurrentDirectory(entry.relativePath);
            context.selection.SetSelectedPath(entry.relativePath);
        }
    }

    private static void DrawList(EditorContext context, IReadOnlyList<AssetFileEntry> entries)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            AssetFileEntry entry = entries[i];
            string icon = entry.isDirectory ? ImGuiIcon.Folder : ImGuiIcon.File;
            string name = Path.GetFileName(entry.relativePath);
            bool selected = string.Equals(context.selection.selectedPath, entry.relativePath, StringComparison.Ordinal);

            if (ImGuiWidget.SelectableIconRow(entry.relativePath, icon, name, selected))
                context.selection.SetSelectedPath(entry.relativePath);

            if (entry.isDirectory
                && NativeImGui.IsItemHovered()
                && NativeImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                context.selection.SetCurrentDirectory(entry.relativePath);
                context.selection.SetSelectedPath(entry.relativePath);
            }
        }
    }

    private static void DrawStatusBar(EditorContext context)
    {
        NativeImGui.TextUnformatted($"Path: {GetPathText(context.selection.currentDirectory)}");
    }

    private static string GetPathText(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return "Assets/";
        return $"Assets/{relativePath}";
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
}
