using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.Commands;

[EditorAction(SceneActionIds.CreateChildGameObject)]
[EditorMenu(typeof(SceneSurface.HierarchyObject), "Create Empty Child", order: 100)]
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
        context.editor.selection.Select(child);
        _ = context.editor.Execute(
            EditorActionIds.Rename,
            typeof(SceneSurface.HierarchyObject),
            child);
    }
}
