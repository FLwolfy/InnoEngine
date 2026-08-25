using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction("hierarchy/set-active-scene")]
[EditorMenu("panel/scene.hierarchy", "Set Active Scene", order: 100)]
internal sealed class SetActiveSceneCommand : EditorAction<GameScene>
{
    protected override EditorActionState Query(EditorActionContext<GameScene> context)
        => context.target.isLoaded
            ? new EditorActionState(true, !ReferenceEquals(context.target, SceneManager.activeScene))
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameScene> context)
        => SceneManager.SetActiveScene(context.target);
}
