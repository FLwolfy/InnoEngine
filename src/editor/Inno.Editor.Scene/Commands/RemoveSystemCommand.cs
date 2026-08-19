using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;
using Inno.Editor.Scene.Inspection;

namespace Inno.Editor.Scene.Commands;

[EditorAction(EditorActionIds.Remove, priority: 100)]
[EditorMenu(typeof(SceneSurface.System), "Remove System", order: 200)]
internal sealed class RemoveSystemCommand : EditorAction<SystemEditorTarget>
{
    protected override EditorActionState Query(EditorActionContext<SystemEditorTarget> context)
        => !context.target.system.isDestroyed
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<SystemEditorTarget> context)
        => _ = context.target.scene.RemoveSystem(context.target.system);
}
