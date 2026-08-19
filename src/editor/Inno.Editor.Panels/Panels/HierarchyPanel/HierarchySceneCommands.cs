using Inno.Editor.Core;
using Inno.Engine.Scene;

namespace Inno.Editor.Panels;

internal sealed class HierarchySceneCommands
{
    internal GameScene Create(EditorContext context)
    {
        GameScene scene = context.sceneWorkspace.CreateScene();
        context.selection.Select(scene);
        return scene;
    }

    internal bool Delete(EditorContext context, GameScene scene)
    {
        if (!context.sceneWorkspace.CloseScene(scene))
            return false;

        if (context.selection.TryGet(out GameScene? selected) && ReferenceEquals(selected, scene))
            context.selection.Clear();
        return true;
    }
}
