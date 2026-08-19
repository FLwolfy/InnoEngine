using Inno.Assets;
using Inno.Assets.Core;
using Inno.Editor.Core.DragDrop;
using Inno.Editor.Assets.DragDrop;

namespace Inno.Editor.Assets.AssetEditors;

/// <summary>Customizes editor interactions for one imported asset type.</summary>
public abstract class AssetEditor
{
    /// <summary>Gets whether the asset can be opened.</summary>
    public virtual bool CanOpen(AssetEditorContext context) => false;

    /// <summary>Opens the asset.</summary>
    public virtual void Open(AssetEditorContext context)
    {
    }

    /// <summary>Validates a requested asset move.</summary>
    public virtual AssetOperationValidation ValidateRename(
        AssetEditorContext context,
        string targetPath) => AssetOperationValidation.valid;

    /// <summary>Runs after an asset move transaction commits.</summary>
    public virtual void OnRenamed(
        AssetEditorContext context,
        string oldPath,
        string newPath)
    {
    }

    /// <summary>Validates a requested asset deletion.</summary>
    public virtual AssetOperationValidation ValidateDelete(AssetEditorContext context)
        => AssetOperationValidation.valid;

    /// <summary>Runs after an asset deletion transaction commits.</summary>
    public virtual void OnDeleted(AssetEditorContext context)
    {
    }

    /// <summary>Gets whether the asset can start an editor drag operation.</summary>
    public virtual bool CanStartDrag(AssetEditorContext context) => !context.isDirectory;

    /// <summary>Creates managed drag data for the asset.</summary>
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
