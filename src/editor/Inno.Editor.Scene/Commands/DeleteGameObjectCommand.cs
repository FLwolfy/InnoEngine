using Inno.Editor.Assets.AssetEditors;
using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;
using Inno.Editor.Scene.Inspection;
using Inno.Editor.Scene.Hierarchy;
using Inno.Core.Input;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.Commands;

[EditorAction(EditorActionIds.Delete, priority: 100)]
[EditorMenu(typeof(SceneSurface.HierarchyObject), "Delete", order: 300)]
[EditorShortcut(typeof(HierarchyPanel), KeyCode.Delete)]
internal sealed class DeleteGameObjectCommand : EditorAction<GameObject>
{
    protected override EditorActionState Query(EditorActionContext<GameObject> context)
        => context.target.isRuntimeValid
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameObject> context)
    {
        if (!context.target.isRuntimeValid)
            return;
        _ = context.target.scene.DestroyObject(context.target);
        if (context.editor.selection.TryGet(out GameObject? selected) && ReferenceEquals(selected, context.target))
            context.editor.selection.Clear();
    }
}
