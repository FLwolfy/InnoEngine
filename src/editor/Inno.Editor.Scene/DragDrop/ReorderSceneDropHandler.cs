using Inno.Editor.Scene.DragDrop;

using Inno.Editor.Scene;

using System;

using Inno.Editor.Core;
using Inno.Editor.Core.DragDrop;
using Inno.Editor.Scene.Inspection;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.DragDrop;

[EditorDrop(typeof(SceneSurface.HierarchyScene))]
internal sealed class ReorderSceneDropHandler
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
        SceneManager.SetSceneIndex(source, insertionIndex);
        _ = context.editor.Select(context.surface, source);
        return EditorDropResult.Accepted(source);
    }
}
