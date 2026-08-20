using Inno.Editor.Interactions.Actions;
using Inno.Editor.Interactions.Menus;
using Inno.Engine.Scene;
using Inno.Editor.Panel.Hierarchy;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Hierarchy.Commands;

[EditorAction(HierarchyActions.CreateGameObject)]
[EditorMenu(HierarchyAreas.Hierarchy, "Create Empty", order: 200)]
internal sealed class CreateGameObjectCommand : EditorAction<GameScene>
{
    protected override EditorActionState Query(EditorActionContext<GameScene> context)
        => context.target.isLoaded
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameScene> context)
    {
        GameObject created = context.target.CreateObject();
        EditorInteraction interaction = context.interactions.For(HierarchyAreas.Hierarchy, created);
        _ = interaction.Select();
        _ = interaction.Execute(HierarchyActions.RenameGameObject);
    }
}
