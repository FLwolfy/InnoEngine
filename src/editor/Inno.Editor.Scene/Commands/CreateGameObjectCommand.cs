using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.Commands;

[EditorAction(SceneActionIds.CreateGameObject)]
[EditorMenu(typeof(SceneSurface.HierarchyScene), "Create Empty", order: 200)]
[EditorMenu(typeof(SceneSurface.HierarchyBlank), "Create Empty", order: 200, separatorBefore: true)]
internal sealed class CreateGameObjectCommand : EditorAction<GameScene>
{
    protected override EditorActionState Query(EditorActionContext<GameScene> context)
        => context.target.isLoaded
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameScene> context)
    {
        GameObject created = context.target.CreateObject();
        _ = context.editor.Select(typeof(SceneSurface.HierarchyObject), created);
        _ = context.editor.Execute(
            EditorActionIds.Rename,
            typeof(SceneSurface.HierarchyObject),
            created);
    }
}
