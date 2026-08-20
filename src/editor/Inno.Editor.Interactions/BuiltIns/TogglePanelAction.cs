using Inno.Editor.Interactions.Actions;
using Inno.Editor.Core.Panels;

namespace Inno.Editor.Interactions.BuiltIns;

[EditorAction(EditorActions.TogglePanel, EditorAreas.MainMenu)]
internal sealed class TogglePanelAction : EditorAction<EditorPanel>
{
    protected override EditorActionState Query(EditorActionContext<EditorPanel> context)
        => new(true, true, context.target.isOpen);

    protected override void Execute(EditorActionContext<EditorPanel> context)
    {
        context.target.isOpen = !context.target.isOpen;
    }
}
