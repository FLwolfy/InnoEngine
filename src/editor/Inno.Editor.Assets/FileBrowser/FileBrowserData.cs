using Inno.Editor.Assets;

using System;
using System.Collections.Generic;
using System.IO;

using Inno.Assets;
using Inno.Assets.File;
using Inno.Editor.Core;

namespace Inno.Editor.Assets.FileBrowser;

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

internal sealed class FileBrowserData(AssetEditorModule assets)
{
    internal IReadOnlyList<AssetFileEntry> CollectVisibleEntries(
        EditorContext context,
        FileBrowserEntryTypeFilter typeFilter,
        FileBrowserEntryScopeFilter scopeFilter,
        string searchFilter)
    {
        List<AssetFileEntry> entries = [];
        if (scopeFilter == FileBrowserEntryScopeFilter.CurrentOnly)
        {
            IReadOnlyList<AssetFileEntry> children = GetVisibleChildren(assets.browser.currentDirectory);
            for (int i = 0; i < children.Count; i++)
                entries.Add(children[i]);
        }
        else
        {
            CollectEntriesRecursive(assets.browser.currentDirectory, entries);
        }

        ApplyTypeFilter(entries, typeFilter);
        ApplySearchFilter(entries, searchFilter);
        entries.Sort(static (left, right) =>
        {
            int byName = string.Compare(
                left.nameWithoutExtension,
                right.nameWithoutExtension,
                StringComparison.OrdinalIgnoreCase);
            if (byName != 0)
                return byName;
            int byExtension = string.Compare(
                left.extension,
                right.extension,
                StringComparison.OrdinalIgnoreCase);
            return byExtension != 0
                ? byExtension
                : string.CompareOrdinal(left.relativePath, right.relativePath);
        });
        return entries;
    }

    private static void ApplyTypeFilter(
        List<AssetFileEntry> entries,
        FileBrowserEntryTypeFilter typeFilter)
    {
        switch (typeFilter)
        {
            case FileBrowserEntryTypeFilter.FoldersOnly:
                entries.RemoveAll(static entry => !entry.isDirectory);
                break;
            case FileBrowserEntryTypeFilter.FilesOnly:
                entries.RemoveAll(static entry => entry.isDirectory);
                break;
        }
    }

    private static void ApplySearchFilter(List<AssetFileEntry> entries, string searchFilter)
    {
        string filter = searchFilter.Trim();
        if (!string.IsNullOrEmpty(filter))
        {
            entries.RemoveAll(entry =>
                !Path.GetFileName(entry.relativePath).Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void CollectEntriesRecursive(string directory, List<AssetFileEntry> entries)
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

    internal List<AssetFileEntry> SortTreeEntries(IReadOnlyList<AssetFileEntry> entries)
    {
        List<AssetFileEntry> sorted = new(entries);
        sorted.Sort(static (left, right) =>
        {
            if (left.isDirectory != right.isDirectory)
                return left.isDirectory ? -1 : 1;
            return string.Compare(
                Path.GetFileName(left.relativePath),
                Path.GetFileName(right.relativePath),
                StringComparison.OrdinalIgnoreCase);
        });
        return sorted;
    }

    internal IReadOnlyList<AssetFileEntry> GetVisibleChildren(string relativePath)
    {
        return AssetManager.GetFileSystemChildren(relativePath);
    }
}
