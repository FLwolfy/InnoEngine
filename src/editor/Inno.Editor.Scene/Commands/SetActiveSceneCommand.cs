using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.Commands;

[EditorAction(SceneActionIds.SetActiveScene)]
[EditorMenu(typeof(SceneSurface.HierarchyScene), "Set Active Scene", order: 100)]
internal sealed class SetActiveSceneCommand : EditorAction<GameScene>
{
    protected override EditorActionState Query(EditorActionContext<GameScene> context)
        => context.target.isLoaded
            ? new EditorActionState(true, !ReferenceEquals(context.target, SceneManager.activeScene))
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameScene> context)
        => SceneManager.SetActiveScene(context.target);
}
