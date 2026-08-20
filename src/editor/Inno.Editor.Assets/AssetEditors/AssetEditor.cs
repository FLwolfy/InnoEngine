using Inno.Assets;
using Inno.Assets.Core;
using Inno.Editor.Core.DragDrop;
using Inno.Editor.Assets.DragDrop;

namespace Inno.Editor.Assets.AssetEditors;

/// <summary>Customizes editor interactions for one imported asset type.</summary>
public abstract class AssetEditor
{
    /// <summary>
    /// Gets whether the fallback asset editor can open the supplied source entry.
    /// </summary>
    /// <param name="context">The immutable source and catalog snapshot for the operation.</param>
    /// <returns><see langword="true"/> when <see cref="Open"/> can handle the entry; otherwise, <see langword="false"/>.</returns>
    public virtual bool CanOpen(AssetEditorContext context) => false;

    /// <summary>
    /// Opens the supplied asset entry when no more specific typed open action handled it.
    /// </summary>
    /// <param name="context">The immutable source and catalog snapshot for the operation.</param>
    public virtual void Open(AssetEditorContext context)
    {
    }

    /// <summary>
    /// Validates a requested asset move before the AssetManager transaction begins.
    /// </summary>
    /// <param name="context">The immutable snapshot of the source entry before the move.</param>
    /// <param name="targetPath">The normalized source-relative destination path.</param>
    /// <returns>A valid result when the transaction may proceed, or a diagnostic describing the rejection.</returns>
    public virtual AssetOperationValidation ValidateRename(
        AssetEditorContext context,
        string targetPath) => AssetOperationValidation.valid;

    /// <summary>
    /// Runs after an asset move transaction commits successfully.
    /// </summary>
    /// <param name="context">The immutable snapshot captured before the move.</param>
    /// <param name="oldPath">The previous source-relative path.</param>
    /// <param name="newPath">The committed source-relative destination path.</param>
    public virtual void OnRenamed(
        AssetEditorContext context,
        string oldPath,
        string newPath)
    {
    }

    /// <summary>
    /// Validates a requested asset deletion before the AssetManager transaction begins.
    /// </summary>
    /// <param name="context">The immutable snapshot of the entry that would be deleted.</param>
    /// <returns>A valid result when the transaction may proceed, or a diagnostic describing the rejection.</returns>
    public virtual AssetOperationValidation ValidateDelete(AssetEditorContext context)
        => AssetOperationValidation.valid;

    /// <summary>
    /// Runs after an asset deletion transaction commits successfully.
    /// </summary>
    /// <param name="context">The immutable snapshot captured before the entry was deleted.</param>
    public virtual void OnDeleted(AssetEditorContext context)
    {
    }

    /// <summary>
    /// Gets whether the supplied entry can begin a managed editor drag operation.
    /// </summary>
    /// <param name="context">The immutable source and catalog snapshot for the entry.</param>
    /// <returns><see langword="true"/> when drag data can be created; otherwise, <see langword="false"/>.</returns>
    public virtual bool CanStartDrag(AssetEditorContext context) => !context.isDirectory;

    /// <summary>
    /// Creates the managed source, preview label, and validity predicate for an asset drag.
    /// </summary>
    /// <param name="context">The immutable source and catalog snapshot for the entry.</param>
    /// <returns>The managed drag data published by the Asset Browser.</returns>
    public virtual EditorDragData CreateDragData(AssetEditorContext context)
        => new(
            new AssetDragSource(
                context.info?.persistentId ?? default,
                context.relativePath,
                context.assetType),
            context.name,
            () => context.info is { persistentId: var id } &&
                  id != System.Guid.Empty &&
                  AssetManager.TryGetInfo(id, out AssetInfo? info) &&
                  info?.status is not AssetImportStatus.Missing and not AssetImportStatus.Conflict);
}
