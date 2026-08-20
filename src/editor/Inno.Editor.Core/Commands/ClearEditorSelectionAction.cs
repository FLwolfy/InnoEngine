namespace Inno.Editor.Core.Commands;

[EditorAction(EditorActionIds.ClearSelection)]
internal sealed class ClearEditorSelectionAction : EditorAction
{
    public override void Execute(EditorActionContext context)
        => context.editor.selection.Clear();
}
