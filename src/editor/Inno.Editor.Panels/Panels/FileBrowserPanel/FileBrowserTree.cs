using System;
using System.Collections.Generic;
using System.IO;

using Inno.Assets;
using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Platform.ImGui;
using static Inno.Editor.Panels.FileBrowserUtility;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

internal sealed class FileBrowserTree
{
    private readonly FileBrowserData m_data;
    private readonly FileBrowserNavigation m_navigation;
    private readonly FileBrowserDragDrop m_dragDrop;
    private readonly Action<string> m_beginRename;
    private readonly Action<string> m_requestDelete;

    private bool m_rootOpenRequest = true;
    private bool m_currentDirectoryOpenRequest;
    private bool m_selectedPathOpenRequest;
    private string m_currentDirectoryOpenTarget = string.Empty;
    private string m_selectedPathOpenTarget = string.Empty;
    private string? m_lastCurrentDirectoryOpenTarget;
    private string? m_lastSelectedPathOpenTarget;

    internal FileBrowserTree(
        FileBrowserData data,
        FileBrowserNavigation navigation,
        FileBrowserDragDrop dragDrop,
        Action<string> beginRename,
        Action<string> requestDelete)
    {
        m_data = data;
        m_navigation = navigation;
        m_dragDrop = dragDrop;
        m_beginRename = beginRename;
        m_requestDelete = requestDelete;
    }

    internal void DrawEntry(
        EditorContext context,
        string relativePath,
        string label,
        bool isRoot)
    {
        List<AssetFileEntry> sorted = m_data.SortTreeEntries(
            m_data.GetVisibleChildren(relativePath));
        bool isDirectory = isRoot || IsDirectoryPath(relativePath);
        bool selected = string.Equals(context.selection.selectedPath, relativePath, StringComparison.Ordinal);
        bool isLeaf = !isDirectory || sorted.Count == 0;
        bool isCurrentDirectory = isDirectory &&
                                  string.Equals(context.selection.currentDirectory, relativePath, StringComparison.Ordinal);
        string icon = isDirectory ? ImGuiIcon.Folder : GetFileIcon(relativePath);
        string nodeId = $"tree_{(isRoot ? "root" : relativePath)}";
        if (ShouldOpenTreeEntry(relativePath, isRoot, isDirectory))
            ImGuiWidget.SetNextTreeNodeOpen(true);

        TreeNodeResult result = ImGuiWidget.TreeNode(
            nodeId,
            () => ImGuiWidget.IconText(icon, label, isCurrentDirectory),
            new TreeNodeOptions { selected = selected, isLeaf = isLeaf });

        AssetFileEntry? treeEntry = null;
        if (!isRoot && AssetManager.TryGetFileSystemEntry(relativePath, out AssetFileEntry resolvedEntry))
            treeEntry = resolvedEntry;
        if (treeEntry is not null)
        {
            DrawContextMenu(context, treeEntry.relativePath);
            m_dragDrop.DrawAssetSource(treeEntry);
        }
        if (result.isDoubleClicked)
        {
            if (treeEntry is not null)
                m_navigation.OpenEntry(context, treeEntry, this);
            else if (isDirectory)
                m_navigation.NavigateTo(context, relativePath, relativePath);
        }
        else if (result.isClicked)
        {
            context.selection.SetSelectedPath(relativePath);
        }
        if (!result.isOpen)
            return;

        for (int i = 0; i < sorted.Count; i++)
        {
            AssetFileEntry child = sorted[i];
            DrawEntry(context, child.relativePath, Path.GetFileName(child.relativePath), false);
        }
        NativeImGui.TreePop();
    }

    private void DrawContextMenu(EditorContext context, string relativePath)
    {
        if (!ImGuiWidget.BeginContextMenu($"##asset_tree_context_{relativePath}"))
            return;
        context.selection.SetSelectedPath(relativePath);
        if (NativeImGui.MenuItem("Rename", "F2"))
            m_beginRename(relativePath);
        if (NativeImGui.MenuItem("Delete", "Delete"))
            m_requestDelete(relativePath);
        ImGuiWidget.EndContextMenu();
    }

    internal void PrepareOpenRequests(EditorContext context)
    {
        string currentDirectory = NormalizePath(context.selection.currentDirectory);
        if (!m_currentDirectoryOpenRequest &&
            !string.Equals(m_lastCurrentDirectoryOpenTarget, currentDirectory, StringComparison.Ordinal))
        {
            m_currentDirectoryOpenTarget = currentDirectory;
            m_currentDirectoryOpenRequest = true;
            m_lastCurrentDirectoryOpenTarget = currentDirectory;
        }

        string selectedTreePath = GetTreeRevealTarget(NormalizePath(context.selection.selectedPath));
        if (!m_selectedPathOpenRequest &&
            !string.Equals(m_lastSelectedPathOpenTarget, selectedTreePath, StringComparison.Ordinal))
        {
            m_selectedPathOpenTarget = selectedTreePath;
            m_selectedPathOpenRequest = true;
            m_lastSelectedPathOpenTarget = selectedTreePath;
        }
    }

    internal void ClearOpenRequests()
    {
        m_rootOpenRequest = false;
        m_currentDirectoryOpenRequest = false;
        m_selectedPathOpenRequest = false;
    }

    internal void RequestRevealPath(string path)
        => RequestOpenTreeToPath(GetTreeRevealTarget(path));

    internal void RequestOpenTreeToPath(string path)
    {
        string normalizedPath = NormalizePath(path);
        string treePath = IsDirectoryPath(normalizedPath) ? normalizedPath : GetParentDirectory(normalizedPath);
        m_selectedPathOpenTarget = treePath;
        m_selectedPathOpenRequest = true;
        m_lastSelectedPathOpenTarget = treePath;
    }

    private static string GetTreeRevealTarget(string path)
        => GetParentDirectory(NormalizePath(path));

    private bool ShouldOpenTreeEntry(
        string relativePath,
        bool isRoot,
        bool isDirectory)
    {
        if (!isDirectory)
            return false;
        if (isRoot)
            return m_rootOpenRequest ||
                   m_currentDirectoryOpenRequest ||
                   m_selectedPathOpenRequest;
        return (m_currentDirectoryOpenRequest &&
                IsAncestorOrSelf(relativePath, m_currentDirectoryOpenTarget)) ||
               (m_selectedPathOpenRequest &&
                IsAncestorOrSelf(relativePath, m_selectedPathOpenTarget));
    }
}
