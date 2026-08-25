using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Global;

[EditorAction("editor/toggle-panel", "editor/main-menu")]
internal sealed class TogglePanelAction : EditorAction
{
    protected override EditorActionState Query(EditorActionContext context)
        => context.TryGetArgument(out EditorPanel? panel)
            ? new EditorActionState(true, true, panel.isOpen)
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext context)
    {
        if (context.TryGetArgument(out EditorPanel? panel))
            panel.isOpen = !panel.isOpen;
    }
}
