using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction("hierarchy/create-child-game-object")]
[EditorMenu("panel/scene.hierarchy", "Create Empty Child", order: 100)]
internal sealed class CreateChildGameObjectCommand(SceneEdits edits) : EditorAction<GameObject>
{
    protected override EditorActionState Query(EditorActionContext<GameObject> context)
        => context.target.isRuntimeValid
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameObject> context)
    {
        GameObject child = edits.CreateGameObject(context.target.scene, context.target.transform);
        EditorInteraction interaction = context.interactions.For("panel/scene.hierarchy", child);
        _ = interaction.Select();
        _ = interaction.Execute("hierarchy/rename-game-object");
    }
}
