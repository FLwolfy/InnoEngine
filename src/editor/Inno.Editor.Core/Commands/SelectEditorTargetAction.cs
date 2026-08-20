namespace Inno.Editor.Core.Commands;

[EditorAction(EditorActionIds.Select)]
internal sealed class SelectEditorTargetAction : EditorAction<object>
{
    protected override void Execute(EditorActionContext<object> context)
        => context.editor.selection.Select(context.target);
}
