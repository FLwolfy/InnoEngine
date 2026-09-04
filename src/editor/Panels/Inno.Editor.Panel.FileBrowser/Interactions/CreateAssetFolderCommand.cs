using System;
using System.Linq;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

[EditorAction(FileBrowserInteractionIds.C_CREATE_FOLDER, FileBrowserInteractionIds.C_AREA)]
[EditorMenu(FileBrowserInteractionIds.C_AREA, "Create/Folder", order: 100)]
internal sealed class CreateAssetFolderCommand(AssetEditorModule assets) : EditorAction<string>
{
    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext<string> context)
        => assets.pipeline.isInitialized && IsWritableDirectory(context.target)
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<string> context)
    {
        string parent = Normalize(context.target);
        string candidate = Combine(parent, "New Folder");
        int suffix = 1;
        while (assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(candidate), out _))
            candidate = Combine(parent, $"New Folder {suffix++}");
        assets.pipeline.CreateDirectory(AssetPath.Parse(candidate));
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
                assets.pipeline.Delete(AssetPath.Parse(candidate));
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
        if (!assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(candidate), out AssetFileEntry target))
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

    private bool IsWritableDirectory(string relativePath)
    {
        string normalized = Normalize(relativePath);
        AssetSourceId source = AssetPath.Parse(normalized).source;
        AssetSourceMount? mount = assets.pipeline.sourceMounts.FirstOrDefault(candidate => candidate.id == source);
        if (mount is null || mount.isReadOnly)
            return false;
        return string.IsNullOrEmpty(AssetPath.Parse(normalized).localPath) ||
               assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(normalized), out AssetFileEntry entry)
               && entry.isDirectory;
    }
}
