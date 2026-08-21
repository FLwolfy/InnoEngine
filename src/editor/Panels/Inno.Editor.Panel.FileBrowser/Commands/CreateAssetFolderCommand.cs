using System;

using Inno.Assets;
using Inno.Assets.File;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

[EditorAction(FileBrowserActions.CreateFolder, FileBrowserAreas.Browser)]
[EditorMenu(FileBrowserAreas.Browser, "Create/Folder", order: 100)]
internal sealed class CreateAssetFolderCommand : EditorAction<string>
{
    protected override EditorActionState Query(EditorActionContext<string> context)
        => AssetManager.isInitialized && IsDirectory(context.target)
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<string> context)
    {
        string parent = Normalize(context.target);
        string candidate = Combine(parent, "New Folder");
        int suffix = 1;
        while (AssetManager.TryGetFileSystemEntry(candidate, out _))
            candidate = Combine(parent, $"New Folder {suffix++}");
        AssetManager.CreateDirectory(candidate);
        var data = new AssetHistoryData(
            AssetHistoryOperationKind.CreateDirectory,
            candidate,
            string.Empty,
            isDirectory: true,
            archive: []);
        context.history.RecordApplied(
            "Create Folder",
            new EditorHistoryChange(
                AssetHistoryKinds.SourceOperation,
                version: 1,
                EditorHistoryPayload.FromBytes(data.Encode())));
        if (!AssetManager.TryGetFileSystemEntry(candidate, out AssetFileEntry target))
            return;
        EditorInteraction interaction = context.interactions.For(FileBrowserAreas.Browser, target);
        _ = interaction.Select();
        _ = interaction.Execute(FileBrowserActions.Rename);
    }

    private static string Combine(string parent, string name)
        => string.IsNullOrEmpty(parent) ? name : $"{parent}/{name}";

    private static string Normalize(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');

    private static bool IsDirectory(string relativePath)
    {
        string normalized = Normalize(relativePath);
        return string.IsNullOrEmpty(normalized) ||
               AssetManager.TryGetFileSystemEntry(normalized, out AssetFileEntry entry) &&
               entry.isDirectory;
    }
}
