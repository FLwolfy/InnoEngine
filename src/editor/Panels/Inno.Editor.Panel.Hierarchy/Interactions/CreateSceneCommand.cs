using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_CREATE_SCENE)]
[EditorMenu(HierarchyInteractionIds.C_AREA, "Create Scene", order: 300, separatorBefore: true)]
internal sealed class CreateSceneCommand(SceneEdits edits) : EditorAction
{
    protected override void Execute(EditorActionContext context)
    {
        GameScene scene = edits.CreateScene();
        EditorInteraction interaction = context.interactions.For(HierarchyInteractionIds.area, scene);
        _ = interaction.Select();
        _ = interaction.Execute(RenameHierarchyTargetCommand.command);
    }
}
