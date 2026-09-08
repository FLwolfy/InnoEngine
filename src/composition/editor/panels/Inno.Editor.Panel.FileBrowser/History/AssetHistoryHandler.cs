using System;

using Inno.Assets;
using Inno.Core.Logging;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

[EditorHistoryHandler(AssetHistoryKinds.SourceOperation)]
internal sealed class AssetHistoryHandler(AssetEditorModule assets, LogRouter logs) : EditorHistoryHandler
{
    private readonly Logger m_log = (logs ?? throw new ArgumentNullException(nameof(logs)))
        .CreateLogger<AssetHistoryHandler>();

    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="change">
    /// The neutral change payload to query or apply.
    /// </param>
    /// <param name="direction">
    /// The history direction that determines which state is applied.
    /// </param>
    /// <returns>
    /// The validated editor history availability that represents the completed operation.
    /// </returns>
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
                AssetHistoryOperationKind.CreateAsset => QueryCreateAsset(data, direction),
                _ => EditorHistoryAvailability.Unavailable("Unknown asset history operation.")
            };
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable($"Asset history payload is invalid: {exception.Message}");
        }
    }

    /// <summary>
    /// Applies a validated change atomically at the caller-controlled commit point.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="change">
    /// The neutral change payload to query or apply.
    /// </param>
    /// <param name="direction">
    /// The history direction that determines which state is applied.
    /// </param>
    /// <returns>
    /// The validated editor history result that represents the completed operation.
    /// </returns>
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
                case AssetHistoryOperationKind.CreateAsset:
                    result = ApplyCreateAsset(data, direction);
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
                m_log.Write(LogLevel.Error, "Asset history selection notification failed: {0}", [exception]);
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
            if (!assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(sourcePath), out _) &&
                assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(targetPath), out _))
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

    private EditorHistoryResult ApplyCreateDirectory(
        AssetHistoryData data,
        EditorHistoryDirection direction)
    {
        bool shouldExist = direction == EditorHistoryDirection.Redo;
        try
        {
            if (shouldExist)
                assets.pipeline.CreateDirectory(AssetPath.Parse(data.sourcePath));
            else
                assets.pipeline.Delete(AssetPath.Parse(data.sourcePath));
            bool exists = assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(data.sourcePath), out _);
            if (exists != shouldExist)
                throw new InvalidOperationException("The folder operation did not reach its requested state.");
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            try
            {
                bool exists = assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(data.sourcePath), out _);
                if (shouldExist && exists)
                    assets.pipeline.Delete(AssetPath.Parse(data.sourcePath));
                else if (!shouldExist && !exists)
                    assets.pipeline.CreateDirectory(AssetPath.Parse(data.sourcePath));
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
                AssetSourceArchive.Restore(assets.pipeline, data.sourcePath, data.isDirectory, data.archive);
            else
                assets.DeleteFromHistory(data.sourcePath);
            bool exists = assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(data.sourcePath), out _);
            if (exists != shouldExist)
                throw new InvalidOperationException("The Asset delete operation did not reach its requested state.");
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            try
            {
                bool exists = assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(data.sourcePath), out _);
                if (shouldExist && exists)
                    assets.pipeline.Delete(AssetPath.Parse(data.sourcePath));
                else if (!shouldExist && !exists)
                    AssetSourceArchive.Restore(assets.pipeline, data.sourcePath, data.isDirectory, data.archive);
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

    private EditorHistoryResult ApplyCreateAsset(
        AssetHistoryData data,
        EditorHistoryDirection direction)
    {
        bool shouldExist = direction == EditorHistoryDirection.Redo;
        try
        {
            if (shouldExist)
                AssetSourceArchive.Restore(assets.pipeline, data.sourcePath, data.isDirectory, data.archive);
            else
                assets.DeleteFromHistory(data.sourcePath);
            bool exists = assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(data.sourcePath), out _);
            if (exists != shouldExist)
                throw new InvalidOperationException("The created Asset did not reach its requested history state.");
            return EditorHistoryResult.Success();
        }
        catch (Exception exception)
        {
            try
            {
                bool exists = assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(data.sourcePath), out _);
                if (shouldExist && exists)
                    assets.DeleteFromHistory(data.sourcePath);
                else if (!shouldExist && !exists)
                    AssetSourceArchive.Restore(assets.pipeline, data.sourcePath, data.isDirectory, data.archive);
            }
            catch (Exception rollbackException)
            {
                return StateIntegrityFailure(
                    $"Asset creation history failed: {exception.Message} " +
                    $"Rollback failed: {rollbackException.Message}");
            }
            return EditorHistoryResult.Failure(exception.Message);
        }
    }

    private EditorHistoryAvailability QueryMove(
        AssetHistoryData data,
        EditorHistoryDirection direction)
    {
        string source = direction == EditorHistoryDirection.Undo ? data.targetPath : data.sourcePath;
        string target = direction == EditorHistoryDirection.Undo ? data.sourcePath : data.targetPath;
        if (!assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(source), out _))
            return EditorHistoryAvailability.Unavailable($"Asset '{source}' no longer exists.");
        return !assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(target), out _)
            ? EditorHistoryAvailability.Available()
            : EditorHistoryAvailability.Unavailable($"Asset '{target}' already exists.");
    }

    private EditorHistoryAvailability QueryCreateDirectory(
        AssetHistoryData data,
        EditorHistoryDirection direction)
        => direction == EditorHistoryDirection.Undo
            ? assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(data.sourcePath), out _)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Folder '{data.sourcePath}' no longer exists.")
            : !assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(data.sourcePath), out _)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Folder '{data.sourcePath}' already exists.");

    private EditorHistoryAvailability QueryDelete(
        AssetHistoryData data,
        EditorHistoryDirection direction)
        => direction == EditorHistoryDirection.Undo
            ? !assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(data.sourcePath), out _)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Asset '{data.sourcePath}' already exists.")
            : assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(data.sourcePath), out _)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Asset '{data.sourcePath}' no longer exists.");

    private EditorHistoryAvailability QueryCreateAsset(
        AssetHistoryData data,
        EditorHistoryDirection direction)
        => direction == EditorHistoryDirection.Undo
            ? assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(data.sourcePath), out _)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Asset '{data.sourcePath}' no longer exists.")
            : !assets.pipeline.TryGetFileSystemEntry(AssetPath.Parse(data.sourcePath), out _)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Asset '{data.sourcePath}' already exists.");
}
