
using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Editor.Core;

namespace Inno.Editor.Panel.FileBrowser;

internal enum FileBrowserEntryTypeFilter
{
    All,
    FoldersOnly,
    FilesOnly
}

internal enum FileBrowserEntryScopeFilter
{
    CurrentOnly,
    Recursive
}

internal readonly record struct FileBrowserDisplayEntry(
    AssetFileEntry entry,
    string displayName,
    bool isPluginRoot = false);

internal sealed class FileBrowserData(AssetEditorModule assets)
{
    internal IReadOnlyList<FileBrowserDisplayEntry> CollectVisibleEntries(
        EditorContext context,
        FileBrowserEntryTypeFilter typeFilter,
        FileBrowserEntryScopeFilter scopeFilter,
        string searchFilter)
    {
        List<FileBrowserDisplayEntry> entries = [];
        if (assets.browser.root == AssetBrowserRoot.Plugins &&
            string.IsNullOrEmpty(assets.browser.currentDirectory))
        {
            CollectPluginRoots(entries, scopeFilter);
        }
        else if (scopeFilter == FileBrowserEntryScopeFilter.CurrentOnly)
        {
            IReadOnlyList<AssetFileEntry> children = GetVisibleChildren(assets.browser.currentDirectory);
            for (int i = 0; i < children.Count; i++)
                entries.Add(new FileBrowserDisplayEntry(children[i], children[i].name));
        }
        else
            CollectEntriesRecursive(assets.browser.currentDirectory, entries);

        ApplyTypeFilter(entries, typeFilter);
        ApplySearchFilter(entries, searchFilter);
        entries.Sort(static (left, right) =>
        {
            int byName = string.Compare(
                left.displayName,
                right.displayName,
                StringComparison.OrdinalIgnoreCase);
            if (byName != 0)
                return byName;
            int byExtension = string.Compare(
                left.entry.extension,
                right.entry.extension,
                StringComparison.OrdinalIgnoreCase);
            return byExtension != 0
                ? byExtension
                : string.CompareOrdinal(
                    left.entry.assetPath.ToString(),
                    right.entry.assetPath.ToString());
        });
        return entries;
    }

    private static void ApplyTypeFilter(
        List<FileBrowserDisplayEntry> entries,
        FileBrowserEntryTypeFilter typeFilter)
    {
        switch (typeFilter)
        {
            case FileBrowserEntryTypeFilter.FoldersOnly:
                entries.RemoveAll(static item => !item.entry.isDirectory);
                break;
            case FileBrowserEntryTypeFilter.FilesOnly:
                entries.RemoveAll(static item => item.entry.isDirectory);
                break;
        }
    }

    private static void ApplySearchFilter(
        List<FileBrowserDisplayEntry> entries,
        string searchFilter)
    {
        string filter = searchFilter.Trim();
        if (!string.IsNullOrEmpty(filter))
        {
            entries.RemoveAll(item =>
                !item.displayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void CollectPluginRoots(
        List<FileBrowserDisplayEntry> entries,
        FileBrowserEntryScopeFilter scopeFilter)
    {
        AssetSourceMount[] mounts = assets.pipeline.sourceMounts
            .Where(mount => assets.IsPluginSource(mount.id))
            .OrderBy(static mount => mount.id.value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (int i = 0; i < mounts.Length; i++)
        {
            AssetSourceMount mount = mounts[i];
            var rootPath = new AssetPath(mount.id, string.Empty);
            if (!assets.pipeline.TryGetFileSystemEntry(rootPath, out AssetFileEntry root))
                continue;
            entries.Add(new FileBrowserDisplayEntry(root, mount.id.value, isPluginRoot: true));
            if (scopeFilter == FileBrowserEntryScopeFilter.Recursive)
                CollectEntriesRecursive(root.assetPath.ToString(), entries);
        }
    }

    private void CollectEntriesRecursive(
        string directory,
        List<FileBrowserDisplayEntry> entries)
    {
        IReadOnlyList<AssetFileEntry> children = GetVisibleChildren(directory);
        for (int i = 0; i < children.Count; i++)
        {
            AssetFileEntry child = children[i];
            entries.Add(new FileBrowserDisplayEntry(child, child.name));
            if (child.isDirectory)
                CollectEntriesRecursive(child.assetPath.ToString(), entries);
        }
    }

    internal List<AssetFileEntry> SortTreeEntries(IReadOnlyList<AssetFileEntry> entries)
    {
        List<AssetFileEntry> sorted = new(entries);
        sorted.Sort(static (left, right) =>
        {
            if (left.isDirectory != right.isDirectory)
                return left.isDirectory ? -1 : 1;
            return string.Compare(
                left.name,
                right.name,
                StringComparison.OrdinalIgnoreCase);
        });
        return sorted;
    }

    internal IReadOnlyList<AssetFileEntry> GetVisibleChildren(string relativePath)
    {
        AssetSourceId source = AssetPath.Parse(relativePath).source;
        IReadOnlyList<AssetFileEntry> children = assets.pipeline.GetFileSystemChildren(AssetPath.Parse(relativePath));
        if (children.Count == 0)
            return children;
        List<AssetFileEntry> isolated = [];
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].source == source)
                isolated.Add(children[i]);
        }
        return isolated;
    }
}
