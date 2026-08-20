using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

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
