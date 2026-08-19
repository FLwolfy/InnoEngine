using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;
using Inno.Editor.Scene.Inspection;

namespace Inno.Editor.Scene.Commands;

[EditorAction(EditorActionIds.Reset, priority: 100)]
[EditorMenu(typeof(SceneSurface.Component), "Reset Component", order: 100)]
internal sealed class ResetComponentCommand : EditorAction<ComponentEditorTarget>
{
    protected override EditorActionState Query(EditorActionContext<ComponentEditorTarget> context)
        => !context.target.component.isDestroyed
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<ComponentEditorTarget> context)
        => context.target.gameObject.ResetComponent(context.target.component);
}
