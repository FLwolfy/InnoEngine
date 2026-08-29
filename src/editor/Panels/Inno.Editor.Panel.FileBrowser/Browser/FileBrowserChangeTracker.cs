
using System;
using System.IO;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Editor.Core;

namespace Inno.Editor.Panel.FileBrowser;

internal sealed class FileBrowserChangeTracker(AssetEditorModule assets)
{
    private EditorContext? m_context;

    internal void Attach(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_context = context;
        AssetManager.Changed -= OnAssetsChanged;
        AssetManager.Changed += OnAssetsChanged;
        AssetManager.SourceMountsChanged -= OnSourceMountsChanged;
        AssetManager.SourceMountsChanged += OnSourceMountsChanged;
    }

    internal void Detach()
    {
        AssetManager.Changed -= OnAssetsChanged;
        AssetManager.SourceMountsChanged -= OnSourceMountsChanged;
        m_context = null;
    }

    private void OnSourceMountsChanged()
    {
        EditorContext? context = m_context;
        if (context is null)
            return;
        string currentDirectory = FileBrowserUtility.NormalizePath(assets.browser.currentDirectory);
        if (!AssetManager.TryGetFileSystemEntry(currentDirectory, out AssetFileEntry current) || !current.isDirectory)
            assets.browser.SetCurrentDirectory(string.Empty);
        string selectedPath = FileBrowserUtility.NormalizePath(assets.browser.GetSelectedPath(context));
        if (!string.IsNullOrEmpty(selectedPath) && !AssetManager.TryGetFileSystemEntry(selectedPath, out _))
            assets.browser.Select(context, null);
    }

    private void OnAssetsChanged(AssetChangeSet changes)
    {
        EditorContext? context = m_context;
        if (context is null)
            return;

        for (int i = 0; i < changes.changes.Count; i++)
        {
            AssetChange change = changes.changes[i];
            if (change.kind == AssetChangeKind.Moved)
                ApplyMove(context, change.oldRelativePath, change.relativePath);
            else if (change.kind is AssetChangeKind.Missing or AssetChangeKind.Removed)
                ApplyRemoval(context, change.relativePath);
        }
    }

    private void ApplyMove(EditorContext context, string oldPath, string newPath)
    {
        oldPath = Normalize(oldPath);
        newPath = Normalize(newPath);
        if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath))
            return;

        string currentDirectory = Normalize(assets.browser.currentDirectory);
        if (IsSameOrDescendant(currentDirectory, oldPath))
        {
            string suffix = currentDirectory.Length == oldPath.Length
                ? string.Empty
                : currentDirectory[oldPath.Length..].TrimStart('/');
            assets.browser.SetCurrentDirectory(Combine(newPath, suffix));
        }

        string selectedPath = Normalize(assets.browser.GetSelectedPath(context));
        if (!IsSameOrDescendant(selectedPath, oldPath))
            return;
        string selectedSuffix = selectedPath.Length == oldPath.Length
            ? string.Empty
            : selectedPath[oldPath.Length..].TrimStart('/');
        assets.browser.Select(context, Combine(newPath, selectedSuffix));
    }

    private void ApplyRemoval(EditorContext context, string removedPath)
    {
        removedPath = Normalize(removedPath);
        if (string.IsNullOrEmpty(removedPath))
            return;

        string selectedPath = Normalize(assets.browser.GetSelectedPath(context));
        if (IsSameOrDescendant(selectedPath, removedPath))
            assets.browser.Select(context, null);

        string currentDirectory = Normalize(assets.browser.currentDirectory);
        if (!IsSameOrDescendant(currentDirectory, removedPath))
            return;

        string fallback = FileBrowserUtility.GetParentDirectory(removedPath);
        while (!string.Equals(
                   fallback,
                   FileBrowserUtility.GetParentDirectory(fallback),
                   StringComparison.Ordinal) &&
               (!AssetManager.TryGetFileSystemEntry(fallback, out var entry) || !entry.isDirectory))
        {
            fallback = FileBrowserUtility.GetParentDirectory(fallback);
        }
        if (!AssetManager.TryGetFileSystemEntry(fallback, out AssetFileEntry available) || !available.isDirectory)
            fallback = string.Empty;
        assets.browser.SetCurrentDirectory(fallback);
    }

    private static bool IsSameOrDescendant(string path, string ancestor)
        => string.Equals(path, ancestor, StringComparison.OrdinalIgnoreCase) ||
           path.StartsWith(ancestor + "/", StringComparison.OrdinalIgnoreCase);

    private static string Combine(string left, string right)
        => string.IsNullOrEmpty(right) ? left : $"{left}/{right}";

    private static string Normalize(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');
}
