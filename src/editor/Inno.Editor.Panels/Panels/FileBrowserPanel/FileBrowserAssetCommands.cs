using System;
using System.IO;

using Inno.Assets;
using Inno.Editor.Core;

namespace Inno.Editor.Panels;

internal sealed class FileBrowserAssetCommands
{
    internal bool CanMutateSelection(EditorContext context)
    {
        string path = Normalize(context.selection.selectedPath);
        return !string.IsNullOrEmpty(path) && AssetManager.TryGetFileSystemEntry(path, out _);
    }

    internal void Rename(EditorContext context, string sourcePath, string newName)
    {
        sourcePath = Normalize(sourcePath);
        newName = newName.Trim();
        if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(newName))
            return;
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            newName.Contains('/') || newName.Contains('\\'))
        {
            throw new ArgumentException(
                "Asset names cannot contain path separators or invalid file-name characters.",
                nameof(newName));
        }

        string parent = Normalize(Path.GetDirectoryName(sourcePath));
        string targetPath = string.IsNullOrEmpty(parent) ? newName : $"{parent}/{newName}";
        AssetManager.Move(sourcePath, targetPath);
        context.selection.SetSelectedPath(targetPath);
    }

    internal void Delete(EditorContext context, string sourcePath)
    {
        sourcePath = Normalize(sourcePath);
        if (string.IsNullOrEmpty(sourcePath))
            return;
        AssetManager.Delete(sourcePath);
        context.selection.SetSelectedPath(null);
    }

    internal string CreateFolder(EditorContext context)
    {
        string parent = Normalize(context.selection.currentDirectory);
        string candidate = Combine(parent, "New Folder");
        int suffix = 1;
        while (AssetManager.TryGetFileSystemEntry(candidate, out _))
            candidate = Combine(parent, $"New Folder {suffix++}");
        AssetManager.CreateDirectory(candidate);
        context.selection.SetSelectedPath(candidate);
        return candidate;
    }

    private static string Combine(string parent, string name)
        => string.IsNullOrEmpty(parent) ? name : $"{parent}/{name}";

    private static string Normalize(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');
}
