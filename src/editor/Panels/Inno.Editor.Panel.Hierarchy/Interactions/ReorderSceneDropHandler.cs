using System;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorDrop("panel/scene.hierarchy")]
internal sealed class ReorderSceneDropHandler(SceneEdits edits)
    : EditorDrop<GameScene, HierarchySceneDropTarget>
{
    protected override EditorDropStatus Query(
        EditorDropContext<GameScene, HierarchySceneDropTarget> context)
    {
        GameScene source = context.source;
        GameScene target = context.target.scene;
        if (ReferenceEquals(source, target) || !source.isLoaded || !target.isLoaded)
            return EditorDropStatus.rejected;
        return EditorDropStatus.Accept(
            context.placement == EditorDropPlacement.After
                ? EditorDropVisual.InsertAfter
                : EditorDropVisual.InsertBefore);
    }

    protected override EditorDropResult Drop(
        EditorDropContext<GameScene, HierarchySceneDropTarget> context)
    {
        GameScene source = context.source;
        GameScene target = context.target.scene;
        int sourceIndex = SceneManager.GetSceneIndex(source);
        int targetIndex = SceneManager.GetSceneIndex(target);
        int insertionIndex = targetIndex + (context.placement == EditorDropPlacement.After ? 1 : 0);
        if (sourceIndex < insertionIndex)
            insertionIndex--;
        edits.SetSceneIndex(source, insertionIndex);
        _ = context.interactions.For(context.area, source).Select();
        return EditorDropResult.Accepted(source);
    }
}
