using System;

using Inno.Assets;
using Inno.Core.Logging;
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
            EditorHistoryResult result;
            string selectionPath = data.sourcePath;
            switch (data.operationKind)
            {
                case AssetHistoryOperationKind.Move:
                    string moveSource = direction == EditorHistoryDirection.Undo
                        ? data.targetPath
                        : data.sourcePath;
                    string moveTarget = direction == EditorHistoryDirection.Undo
                        ? data.sourcePath
                        : data.targetPath;
                    result = ApplyMove(moveSource, moveTarget);
                    selectionPath = moveTarget;
                    break;
                case AssetHistoryOperationKind.CreateDirectory:
                    result = ApplyCreateDirectory(data, direction);
                    break;
                case AssetHistoryOperationKind.Delete:
                    result = ApplyDelete(data, direction);
                    break;
                default:
                    return EditorHistoryResult.Failure("Unknown asset history operation.");
            }
            if (!result.succeeded)
                return result;
            try
            {
                assets.SelectPath(selectionPath);
            }
            catch (Exception exception)
            {
                Log.Error("Asset history selection notification failed: {0}", exception);
            }
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            return EditorHistoryResult.Failure(exception.Message);
        }
    }

    private EditorHistoryResult ApplyMove(string sourcePath, string targetPath)
    {
        try
        {
            assets.MoveFromHistory(sourcePath, targetPath);
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            if (!AssetManager.TryGetFileSystemEntry(sourcePath, out _) &&
                AssetManager.TryGetFileSystemEntry(targetPath, out _))
            {
                try
                {
                    assets.MoveFromHistory(targetPath, sourcePath);
                }
                catch (Exception rollbackException)
                {
                    return StateIntegrityFailure(
                        $"Asset move failed: {exception.Message} " +
                        $"Rollback failed: {rollbackException.Message}");
                }
            }
            return EditorHistoryResult.Failure(exception.Message);
        }
    }

    private static EditorHistoryResult ApplyCreateDirectory(
        AssetHistoryData data,
        EditorHistoryDirection direction)
    {
        bool shouldExist = direction == EditorHistoryDirection.Redo;
        try
        {
            if (shouldExist)
                AssetManager.CreateDirectory(data.sourcePath);
            else
                AssetManager.Delete(data.sourcePath);
            bool exists = AssetManager.TryGetFileSystemEntry(data.sourcePath, out _);
            if (exists != shouldExist)
                throw new InvalidOperationException("The folder operation did not reach its requested state.");
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            try
            {
                bool exists = AssetManager.TryGetFileSystemEntry(data.sourcePath, out _);
                if (shouldExist && exists)
                    AssetManager.Delete(data.sourcePath);
                else if (!shouldExist && !exists)
                    AssetManager.CreateDirectory(data.sourcePath);
            }
            catch (Exception rollbackException)
            {
                return StateIntegrityFailure(
                    $"Asset folder operation failed: {exception.Message} " +
                    $"Rollback failed: {rollbackException.Message}");
            }
            return EditorHistoryResult.Failure(exception.Message);
        }
    }

    private EditorHistoryResult ApplyDelete(
        AssetHistoryData data,
        EditorHistoryDirection direction)
    {
        bool shouldExist = direction == EditorHistoryDirection.Undo;
        try
        {
            if (shouldExist)
                AssetSourceArchive.Restore(data.sourcePath, data.isDirectory, data.archive);
            else
                assets.DeleteFromHistory(data.sourcePath);
            bool exists = AssetManager.TryGetFileSystemEntry(data.sourcePath, out _);
            if (exists != shouldExist)
                throw new InvalidOperationException("The Asset delete operation did not reach its requested state.");
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            try
            {
                bool exists = AssetManager.TryGetFileSystemEntry(data.sourcePath, out _);
                if (shouldExist && exists)
                    AssetManager.Delete(data.sourcePath);
                else if (!shouldExist && !exists)
                    AssetSourceArchive.Restore(data.sourcePath, data.isDirectory, data.archive);
            }
            catch (Exception rollbackException)
            {
                return StateIntegrityFailure(
                    $"Asset delete operation failed: {exception.Message} " +
                    $"Rollback failed: {rollbackException.Message}");
            }
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
