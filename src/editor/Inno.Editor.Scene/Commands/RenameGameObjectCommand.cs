using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;
using Inno.Editor.Scene.Hierarchy;
using Inno.Editor.Scene.Workspace;
using Inno.Core.Input;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.Commands;

[EditorAction(EditorActionIds.Rename, priority: 100)]
[EditorMenu(typeof(SceneSurface.HierarchyObject), "Rename", order: 200)]
[EditorShortcut(typeof(HierarchyPanel), KeyCode.F2)]
internal sealed class RenameGameObjectCommand(EditorSceneWorkspace workspace) : EditorAction<GameObject>
{
    protected override EditorActionState Query(EditorActionContext<GameObject> context)
        => context.target.isRuntimeValid
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<GameObject> context)
        => workspace.BeginRename(context.target);
}
