using System;
using System.IO;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Editor.Core;

namespace Inno.Editor.Panels;

internal sealed class FileBrowserChangeTracker
{
    private EditorContext? m_context;

    internal void Attach(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_context = context;
        AssetManager.Changed -= OnAssetsChanged;
        AssetManager.Changed += OnAssetsChanged;
    }

    internal void Detach()
    {
        AssetManager.Changed -= OnAssetsChanged;
        m_context = null;
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

    private static void ApplyMove(EditorContext context, string oldPath, string newPath)
    {
        oldPath = Normalize(oldPath);
        newPath = Normalize(newPath);
        if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath))
            return;

        string currentDirectory = Normalize(context.selection.currentDirectory);
        if (IsSameOrDescendant(currentDirectory, oldPath))
        {
            string suffix = currentDirectory.Length == oldPath.Length
                ? string.Empty
                : currentDirectory[oldPath.Length..].TrimStart('/');
            context.selection.SetCurrentDirectory(Combine(newPath, suffix));
        }

        string selectedPath = Normalize(context.selection.selectedPath);
        if (!IsSameOrDescendant(selectedPath, oldPath))
            return;
        string selectedSuffix = selectedPath.Length == oldPath.Length
            ? string.Empty
            : selectedPath[oldPath.Length..].TrimStart('/');
        context.selection.SetSelectedPath(Combine(newPath, selectedSuffix));
    }

    private static void ApplyRemoval(EditorContext context, string removedPath)
    {
        removedPath = Normalize(removedPath);
        if (string.IsNullOrEmpty(removedPath))
            return;

        string selectedPath = Normalize(context.selection.selectedPath);
        if (IsSameOrDescendant(selectedPath, removedPath))
            context.selection.SetSelectedPath(null);

        string currentDirectory = Normalize(context.selection.currentDirectory);
        if (!IsSameOrDescendant(currentDirectory, removedPath))
            return;

        string fallback = Normalize(Path.GetDirectoryName(removedPath));
        while (!string.IsNullOrEmpty(fallback) &&
               (!AssetManager.TryGetFileSystemEntry(fallback, out var entry) || !entry.isDirectory))
        {
            fallback = Normalize(Path.GetDirectoryName(fallback));
        }
        context.selection.SetCurrentDirectory(fallback);
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
