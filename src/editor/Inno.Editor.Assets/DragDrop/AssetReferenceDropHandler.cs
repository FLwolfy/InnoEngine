using Inno.Editor.Assets;

using Inno.Editor.Core.DragDrop;

namespace Inno.Editor.Assets.DragDrop;

[EditorDrop(typeof(AssetSurface.Reference), priority: 100)]
internal sealed class AssetReferenceDropHandler
    : EditorDrop<AssetDragSource, AssetReferenceDropTarget>
{
    protected override EditorDropStatus Query(
        EditorDropContext<AssetDragSource, AssetReferenceDropTarget> context)
    {
        if (context.source.assetType is null ||
            context.source.persistentId == System.Guid.Empty ||
            !context.target.expectedType.IsAssignableFrom(context.source.assetType))
        {
            return EditorDropStatus.rejected;
        }
        return EditorDropStatus.Accept();
    }

    protected override EditorDropResult Drop(
        EditorDropContext<AssetDragSource, AssetReferenceDropTarget> context)
    {
        if (!Query(context).canDrop)
            return EditorDropResult.rejected;
        context.target.Assign(context.source.persistentId);
        return EditorDropResult.Accepted();
    }
}
