using Inno.Editor.Interactions;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyActions.CreateChildGameObject)]
[EditorMenu(HierarchyAreas.Hierarchy, "Create Empty Child", order: 100)]
internal sealed class CreateChildGameObjectCommand : EditorAction<GameObject>
{
    protected override EditorActionState Query(EditorActionContext<GameObject> context)
        => context.target.isRuntimeValid
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameObject> context)
    {
        GameObject child = context.target.scene.CreateObject();
        child.transform.SetParent(context.target.transform);
        EditorInteraction interaction = context.interactions.For(HierarchyAreas.Hierarchy, child);
        _ = interaction.Select();
        _ = interaction.Execute(HierarchyActions.RenameGameObject);
    }
}
