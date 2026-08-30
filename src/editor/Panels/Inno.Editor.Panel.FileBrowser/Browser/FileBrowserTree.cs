
using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Core;
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

internal sealed class FileBrowserTree
{
    private readonly FileBrowserData m_data;
    private readonly FileBrowserNavigation m_navigation;
    private readonly FileBrowserDragDrop m_dragDrop;
    private readonly FileBrowserRename m_rename;
    private readonly FileBrowserContextMenu m_contextMenu;
    private readonly AssetEditorModule m_assets;

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
        FileBrowserRename rename,
        FileBrowserContextMenu contextMenu,
        AssetEditorModule assets)
    {
        m_data = data;
        m_navigation = navigation;
        m_dragDrop = dragDrop;
        m_rename = rename;
        m_contextMenu = contextMenu;
        m_assets = assets;
    }

    internal void DrawEntry(
        EditorContext context,
        string relativePath,
        string label,
        bool isRoot,
        bool isReadOnlyRoot = false)
    {
        List<AssetFileEntry> sorted = m_data.SortTreeEntries(
            m_data.GetVisibleChildren(relativePath));
        bool isDirectory = isRoot || IsDirectoryPath(relativePath);
        bool selected = string.Equals(m_assets.browser.GetSelectedPath(context), relativePath, StringComparison.Ordinal);
        bool isLeaf = !isDirectory || sorted.Count == 0;
        bool isCurrentDirectory = isDirectory &&
                                  string.Equals(m_assets.browser.currentDirectory, relativePath, StringComparison.Ordinal);
        string icon = m_assets.folderIcon;
        if (!isDirectory
            && AssetManager.TryGetFileSystemEntry(AssetPath.Parse(relativePath), out AssetFileEntry iconEntry))
            icon = m_assets.GetIcon(iconEntry);
        bool editing = !isRoot && m_rename.IsEditing(
            context,
            relativePath,
            FileBrowserPresentation.Tree);
        string nodeId = $"tree_{(isRoot ? $"root_{relativePath}" : relativePath)}";
        if (ShouldOpenTreeEntry(relativePath, isRoot, isDirectory))
            EditorWidget.SetNextTreeNodeOpen(true);

        TreeNodeResult result = EditorWidget.TreeNode(
            nodeId,
            drawContext => DrawRowContent(
                context,
                nodeId,
                relativePath,
                label,
                icon,
                isCurrentDirectory,
                editing,
                drawContext.rowHeight),
            new TreeNodeOptions { selected = selected, isLeaf = isLeaf });

        AssetFileEntry? treeEntry = null;
        if (!isRoot
            && AssetManager.TryGetFileSystemEntry(AssetPath.Parse(relativePath), out AssetFileEntry resolvedEntry))
            treeEntry = resolvedEntry;
        if (treeEntry is not null && result.isDoubleClicked)
        {
            m_rename.MarkInteraction(FileBrowserPresentation.Tree);
            m_navigation.OpenEntry(context, treeEntry, this);
        }
        else if (treeEntry is not null && result.isClicked)
        {
            m_rename.MarkInteraction(FileBrowserPresentation.Tree);
            m_assets.browser.Select(context, relativePath);
        }
        else if (isRoot && (result.isClicked || result.isDoubleClicked))
        {
            m_navigation.NavigateTo(context, relativePath);
            m_assets.browser.Select(context, null);
        }
        if (treeEntry is not null)
        {
            if (!editing)
            {
                m_dragDrop.DrawAssetSource(context, treeEntry);
                if (!treeEntry.isReadOnly)
                {
                    m_contextMenu.DrawEntry(
                        context,
                        $"##asset_tree_context_{treeEntry.assetPath.ToString()}",
                        treeEntry.assetPath.ToString(),
                        FileBrowserPresentation.Tree);
                }
                if (treeEntry.isDirectory && !treeEntry.isReadOnly)
                    m_dragDrop.DrawDirectoryTarget(context, treeEntry.assetPath.ToString());
            }
        }
        else if (isRoot && !isReadOnlyRoot)
        {
            m_contextMenu.DrawDirectory(
                context,
                $"##asset_tree_root_context_{relativePath}",
                relativePath,
                FileBrowserPresentation.Tree);
            m_dragDrop.DrawDirectoryTarget(context, relativePath);
        }
        if (!result.isOpen)
            return;

        for (int i = 0; i < sorted.Count; i++)
        {
            AssetFileEntry child = sorted[i];
            DrawEntry(context, child.assetPath.ToString(), child.name, false);
        }
        NativeImGui.TreePop();
    }

    internal void DrawPluginRoot(
        EditorContext context,
        IReadOnlyList<AssetSourceMount> sourceMounts)
    {
        AssetSourceMount[] plugins = sourceMounts
            .Where(static mount => mount.id != AssetSourceId.project)
            .OrderBy(static mount => mount.id.value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AssetPath current = AssetPath.Parse(m_assets.browser.currentDirectory);
        string? selectedPath = m_assets.browser.GetSelectedPath(context);
        bool hasPluginNavigation = current.source != AssetSourceId.project
            || (!string.IsNullOrWhiteSpace(selectedPath)
                && AssetPath.Parse(selectedPath).source != AssetSourceId.project);
        if (m_rootOpenRequest || hasPluginNavigation)
            EditorWidget.SetNextTreeNodeOpen(true);

        TreeNodeResult result = EditorWidget.TreeNode(
            "tree_virtual_plugins",
            _ => EditorWidget.IconText(m_assets.folderIcon, "Plugins", false),
            new TreeNodeOptions { isLeaf = plugins.Length == 0 });
        if (!result.isOpen)
            return;
        for (int i = 0; i < plugins.Length; i++)
        {
            AssetSourceMount mount = plugins[i];
            string root = new AssetPath(mount.id, string.Empty).ToString();
            DrawEntry(context, root, mount.id.value, true, mount.isReadOnly);
        }
        NativeImGui.TreePop();
    }

    private void DrawRowContent(
        EditorContext context,
        string id,
        string relativePath,
        string label,
        string icon,
        bool isCurrentDirectory,
        bool editing,
        float rowHeight)
    {
        if (!editing)
        {
            EditorWidget.IconText(icon, label, isCurrentDirectory);
            return;
        }

        EditorWidget.IconText(icon, string.Empty, false);
        NativeImGui.SameLine(0f, 0f);
        m_rename.Draw(
            context,
            id,
            relativePath,
            FileBrowserPresentation.Tree,
            NativeImGui.GetContentRegionAvail().X,
            rowHeight);
    }

    internal void PrepareOpenRequests(EditorContext context)
    {
        string currentDirectory = NormalizePath(m_assets.browser.currentDirectory);
        if (!m_currentDirectoryOpenRequest &&
            !string.Equals(m_lastCurrentDirectoryOpenTarget, currentDirectory, StringComparison.Ordinal))
        {
            m_currentDirectoryOpenTarget = currentDirectory;
            m_currentDirectoryOpenRequest = true;
            m_lastCurrentDirectoryOpenTarget = currentDirectory;
        }

        string selectedTreePath = GetTreeRevealTarget(NormalizePath(m_assets.browser.GetSelectedPath(context)));
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
