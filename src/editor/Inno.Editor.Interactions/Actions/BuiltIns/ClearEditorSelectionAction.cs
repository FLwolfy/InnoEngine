
namespace Inno.Editor.Interactions;

[EditorAction(EditorActions.ClearSelection)]
internal sealed class ClearEditorSelectionAction : EditorAction
{
    protected override void Execute(EditorActionContext context)
    {
        context.interactions.PrepareSelectionChange(null);
        context.interactions.selection.Clear();
    }
}
