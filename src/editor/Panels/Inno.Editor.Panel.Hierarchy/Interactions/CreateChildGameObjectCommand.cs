using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_CREATE_CHILD)]
[EditorMenu(HierarchyInteractionIds.C_AREA, "Create Empty Child", order: 100)]
internal sealed class CreateChildGameObjectCommand(SceneEdits edits) : EditorAction<GameObject>
{
    protected override EditorActionState Query(EditorActionContext<GameObject> context)
        => context.target.isRuntimeValid
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameObject> context)
    {
        GameObject child = edits.CreateGameObject(context.target.scene, context.target.transform);
        EditorInteraction interaction = context.interactions.For(HierarchyInteractionIds.area, child);
        _ = interaction.Select();
        _ = interaction.Execute(RenameHierarchyTargetCommand.command);
    }
}
