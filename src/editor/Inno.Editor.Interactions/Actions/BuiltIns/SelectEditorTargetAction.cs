
namespace Inno.Editor.Interactions;

[EditorAction(EditorActions.Select)]
internal sealed class SelectEditorTargetAction : EditorAction<object>
{
    protected override void Execute(EditorActionContext<object> context)
    {
        context.interactions.PrepareSelectionChange(context.target);
        context.interactions.selection.Select(context.target);
    }
}
