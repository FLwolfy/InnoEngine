using System;

using Inno.Assets;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

[EditorHistoryHandler(AssetHistoryKinds.SourceOperation)]
internal sealed class AssetHistoryHandler(AssetEditorModule assets) : EditorHistoryHandler
{
    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            AssetHistoryData data = AssetHistoryData.Decode(change.payload.ReadBytes());
            return data.operationKind switch
            {
                AssetHistoryOperationKind.Move => QueryMove(data, direction),
                AssetHistoryOperationKind.CreateDirectory => QueryCreateDirectory(data, direction),
                AssetHistoryOperationKind.Delete => QueryDelete(data, direction),
                _ => EditorHistoryAvailability.Unavailable("Unknown asset history operation.")
            };
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable($"Asset history payload is invalid: {exception.Message}");
        }
    }

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            AssetHistoryData data = AssetHistoryData.Decode(change.payload.ReadBytes());
            switch (data.operationKind)
            {
                case AssetHistoryOperationKind.Move:
                    assets.MoveFromHistory(
                        direction == EditorHistoryDirection.Undo ? data.targetPath : data.sourcePath,
                        direction == EditorHistoryDirection.Undo ? data.sourcePath : data.targetPath);
                    break;
                case AssetHistoryOperationKind.CreateDirectory:
                    if (direction == EditorHistoryDirection.Undo)
                        AssetManager.Delete(data.sourcePath);
                    else
                        AssetManager.CreateDirectory(data.sourcePath);
                    break;
                case AssetHistoryOperationKind.Delete:
                    if (direction == EditorHistoryDirection.Undo)
                        AssetSourceArchive.Restore(data.sourcePath, data.isDirectory, data.archive);
                    else
                        assets.DeleteFromHistory(data.sourcePath);
                    break;
                default:
                    return EditorHistoryResult.Failure("Unknown asset history operation.");
            }
            assets.SelectPath(data.operationKind == AssetHistoryOperationKind.Move &&
                              direction == EditorHistoryDirection.Redo
                ? data.targetPath
                : data.sourcePath);
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }
    }

    private static EditorHistoryAvailability QueryMove(
        AssetHistoryData data,
        EditorHistoryDirection direction)
    {
        string source = direction == EditorHistoryDirection.Undo ? data.targetPath : data.sourcePath;
        string target = direction == EditorHistoryDirection.Undo ? data.sourcePath : data.targetPath;
        if (!AssetManager.TryGetFileSystemEntry(source, out _))
            return EditorHistoryAvailability.Unavailable($"Asset '{source}' no longer exists.");
        return !AssetManager.TryGetFileSystemEntry(target, out _)
            ? EditorHistoryAvailability.Available()
            : EditorHistoryAvailability.Unavailable($"Asset '{target}' already exists.");
    }

    private static EditorHistoryAvailability QueryCreateDirectory(
        AssetHistoryData data,
        EditorHistoryDirection direction)
        => direction == EditorHistoryDirection.Undo
            ? AssetManager.TryGetFileSystemEntry(data.sourcePath, out _)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Folder '{data.sourcePath}' no longer exists.")
            : !AssetManager.TryGetFileSystemEntry(data.sourcePath, out _)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Folder '{data.sourcePath}' already exists.");

    private static EditorHistoryAvailability QueryDelete(
        AssetHistoryData data,
        EditorHistoryDirection direction)
        => direction == EditorHistoryDirection.Undo
            ? !AssetManager.TryGetFileSystemEntry(data.sourcePath, out _)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Asset '{data.sourcePath}' already exists.")
            : AssetManager.TryGetFileSystemEntry(data.sourcePath, out _)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Asset '{data.sourcePath}' no longer exists.");
}
