using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;
using Inno.Editor.Scene.Inspection;

namespace Inno.Editor.Scene.Commands;

[EditorAction(EditorActionIds.Reset, priority: 100)]
[EditorMenu(typeof(SceneSurface.System), "Reset System", order: 100)]
internal sealed class ResetSystemCommand : EditorAction<SystemEditorTarget>
{
    protected override EditorActionState Query(EditorActionContext<SystemEditorTarget> context)
        => !context.target.system.isDestroyed
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<SystemEditorTarget> context)
        => context.target.scene.ResetSystem(context.target.system);
}
