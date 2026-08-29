using System;
using System.Linq;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

[EditorAction(FileBrowserInteractionIds.C_CREATE_FOLDER, FileBrowserInteractionIds.C_AREA)]
[EditorMenu(FileBrowserInteractionIds.C_AREA, "Create/Folder", order: 100)]
internal sealed class CreateAssetFolderCommand : EditorAction<string>
{
    protected override EditorActionState Query(EditorActionContext<string> context)
        => AssetManager.isInitialized && IsWritableDirectory(context.target)
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<string> context)
    {
        string parent = Normalize(context.target);
        string candidate = Combine(parent, "New Folder");
        int suffix = 1;
        while (AssetManager.TryGetFileSystemEntry(AssetPath.Parse(candidate), out _))
            candidate = Combine(parent, $"New Folder {suffix++}");
        AssetManager.CreateDirectory(AssetPath.Parse(candidate));
        var data = new AssetHistoryData(
            AssetHistoryOperationKind.CreateDirectory,
            candidate,
            string.Empty,
            isDirectory: true,
            archive: []);
        try
        {
            context.history.RecordApplied(
                "Create Folder",
                new EditorHistoryChange(
                    AssetHistoryKinds.SourceOperation,
                    EditorHistoryPayload.FromBytes(data.Encode())));
        }
        catch (Exception failure)
        {
            try
            {
                AssetManager.Delete(AssetPath.Parse(candidate));
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "The folder could not be recorded and its compensation also failed.",
                    failure,
                    rollbackException);
            }
            throw;
        }
        if (!AssetManager.TryGetFileSystemEntry(AssetPath.Parse(candidate), out AssetFileEntry target))
            return;
        EditorInteraction interaction = context.interactions.For(FileBrowserInteractionIds.C_AREA, target);
        _ = interaction.Select();
        _ = interaction.Execute(FileBrowserInteractionIds.C_RENAME);
    }

    private static string Combine(string parent, string name)
        => string.IsNullOrEmpty(parent) ? name : $"{parent}/{name}";

    private static string Normalize(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');

    private static bool IsWritableDirectory(string relativePath)
    {
        string normalized = Normalize(relativePath);
        AssetSourceId source = AssetPath.Parse(normalized).source;
        AssetSourceMount? mount = AssetManager.sourceMounts.FirstOrDefault(candidate => candidate.id == source);
        if (mount is null || mount.isReadOnly)
            return false;
        return string.IsNullOrEmpty(AssetPath.Parse(normalized).localPath) ||
               AssetManager.TryGetFileSystemEntry(AssetPath.Parse(normalized), out AssetFileEntry entry)
               && entry.isDirectory;
    }
}
