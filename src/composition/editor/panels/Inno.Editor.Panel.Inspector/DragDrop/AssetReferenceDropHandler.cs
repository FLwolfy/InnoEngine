using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Inspector;

[EditorDrop(InspectorInteractionIds.C_ASSET_REFERENCE_AREA, priority: 100)]
internal sealed class AssetReferenceDropHandler(AssetPipeline assets)
    : EditorDrop<AssetFileEntry, AssetReferenceDropTarget>
{
    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor drop status that represents the completed operation.
    /// </returns>
    protected override EditorDropStatus Query(
        EditorDropContext<AssetFileEntry, AssetReferenceDropTarget> context)
    {
        if (!assets.TryGetPersistentId(context.source.assetPath, out System.Guid persistentId) ||
            persistentId == System.Guid.Empty ||
            !assets.TryGetAssetType(context.source.assetPath, out System.Type? assetType) ||
            assetType is null ||
            !context.target.expectedType.IsAssignableFrom(assetType))
        {
            return EditorDropStatus.rejected;
        }
        return EditorDropStatus.Accept();
    }

    /// <summary>
    /// Validates and applies the current editor drag-and-drop interaction atomically.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor drop result that represents the completed operation.
    /// </returns>
    protected override EditorDropResult Drop(
        EditorDropContext<AssetFileEntry, AssetReferenceDropTarget> context)
    {
        if (!Query(context).canDrop)
            return EditorDropResult.rejected;
        if (!assets.TryGetPersistentId(context.source.assetPath, out System.Guid persistentId))
            return EditorDropResult.rejected;
        context.target.Assign(persistentId);
        return EditorDropResult.Accepted();
    }
}
