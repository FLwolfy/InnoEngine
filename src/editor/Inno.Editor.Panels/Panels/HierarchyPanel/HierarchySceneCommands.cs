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
}
