using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;
using Inno.Editor.Scene.Workspace;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.Commands;

[EditorAction(SceneActionIds.CreateScene)]
[EditorMenu(typeof(SceneSurface.HierarchyScene), "Create Scene", order: 300, separatorBefore: true)]
[EditorMenu(typeof(SceneSurface.HierarchyBlank), "Create Scene", order: 100)]
internal sealed class CreateSceneCommand(EditorSceneWorkspace workspace) : EditorAction
{
    public override void Execute(EditorActionContext context)
    {
        GameScene scene = workspace.CreateScene();
        _ = context.editor.Select(context.surface, scene);
    }
}
