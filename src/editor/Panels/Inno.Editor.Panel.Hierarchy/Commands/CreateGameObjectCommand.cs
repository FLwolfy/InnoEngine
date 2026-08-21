using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyActions.CreateGameObject)]
[EditorMenu(HierarchyAreas.Hierarchy, "Create Empty", order: 200)]
internal sealed class CreateGameObjectCommand(SceneEdits edits) : EditorAction<GameScene>
{
    protected override EditorActionState Query(EditorActionContext<GameScene> context)
        => context.target.isLoaded
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameScene> context)
    {
        GameObject created = edits.CreateGameObject(context.target);
        EditorInteraction interaction = context.interactions.For(HierarchyAreas.Hierarchy, created);
        _ = interaction.Select();
        _ = interaction.Execute(HierarchyActions.RenameGameObject);
    }
}
