using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction("hierarchy/create-game-object")]
[EditorMenu("panel/scene.hierarchy", "Create Empty", order: 200)]
internal sealed class CreateGameObjectCommand(SceneEdits edits) : EditorAction<GameScene>
{
    protected override EditorActionState Query(EditorActionContext<GameScene> context)
        => context.target.isLoaded
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameScene> context)
    {
        GameObject created = edits.CreateGameObject(context.target);
        EditorInteraction interaction = context.interactions.For("panel/scene.hierarchy", created);
        _ = interaction.Select();
        _ = interaction.Execute("hierarchy/rename-game-object");
    }
}
