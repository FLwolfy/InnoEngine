using Inno.Assets;
using Inno.Assets.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Inspector;

[EditorDrop(InspectorAreas.AssetReference, priority: 100)]
internal sealed class AssetReferenceDropHandler
    : EditorDrop<AssetInfo, AssetReferenceDropTarget>
{
    protected override EditorDropStatus Query(
        EditorDropContext<AssetInfo, AssetReferenceDropTarget> context)
    {
        if (context.source.persistentId == System.Guid.Empty ||
            !AssetManager.TryGetAssetType(context.source.relativePath, out System.Type? assetType) ||
            assetType is null ||
            !context.target.expectedType.IsAssignableFrom(assetType))
        {
            return EditorDropStatus.rejected;
        }
        return EditorDropStatus.Accept();
    }

    protected override EditorDropResult Drop(
        EditorDropContext<AssetInfo, AssetReferenceDropTarget> context)
    {
        if (!Query(context).canDrop)
            return EditorDropResult.rejected;
        context.target.Assign(context.source.persistentId);
        return EditorDropResult.Accepted();
    }
}
