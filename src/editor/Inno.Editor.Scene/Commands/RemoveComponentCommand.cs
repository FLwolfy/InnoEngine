using Inno.Editor.Core.Commands;
using Inno.Editor.Core.Menus;
using Inno.Editor.Scene.Inspection;
using Inno.Engine.Scene.Components;

namespace Inno.Editor.Scene.Commands;

[EditorAction(EditorActionIds.Remove, priority: 100)]
[EditorMenu(typeof(SceneSurface.Component), "Remove Component", order: 200)]
internal sealed class RemoveComponentCommand : EditorAction<ComponentEditorTarget>
{
    protected override EditorActionState Query(EditorActionContext<ComponentEditorTarget> context)
        => context.target.component is not Transform
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<ComponentEditorTarget> context)
    {
        if (context.target.component is not Transform)
            _ = context.target.gameObject.RemoveComponent(context.target.component);
    }
}
