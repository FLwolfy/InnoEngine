using Inno.Editor.Scene.DragDrop;

using Inno.Editor.Scene;

using System;

using Inno.Editor.Core;
using Inno.Editor.Core.DragDrop;
using Inno.Editor.Scene.Inspection;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.DragDrop;

[EditorDrop(typeof(SceneSurface.HierarchyScene))]
[EditorDrop(typeof(SceneSurface.HierarchyBlank))]
internal sealed class MoveGameObjectToSceneDropHandler
    : EditorDrop<GameObject, HierarchySceneDropTarget>
{
    protected override EditorDropStatus Query(
        EditorDropContext<GameObject, HierarchySceneDropTarget> context)
    {
        GameObject source = context.source;
        GameScene target = context.target.scene;
        return source.isRuntimeValid && ReferenceEquals(source.scene, target)
            ? EditorDropStatus.Accept()
            : EditorDropStatus.rejected;
    }

    protected override EditorDropResult Drop(
        EditorDropContext<GameObject, HierarchySceneDropTarget> context)
    {
        GameObject source = context.source;
        GameScene target = context.target.scene;
        source.transform.SetParent(null);
        source.transform.SetSiblingIndex(GetRootCount(target) - 1);
        context.editor.selection.Select(source);
        return EditorDropResult.Accepted(source, target);
    }

    private static int GetRootCount(GameScene scene)
    {
        int count = 0;
        foreach (GameObject gameObject in scene.GetObjects())
        {
            if (gameObject.transform.parent is null)
                count++;
        }
        return count;
    }
}
