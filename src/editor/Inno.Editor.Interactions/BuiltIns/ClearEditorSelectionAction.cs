using Inno.Editor.Interactions.Actions;

namespace Inno.Editor.Interactions.BuiltIns;

[EditorAction(EditorActions.ClearSelection)]
internal sealed class ClearEditorSelectionAction : EditorAction
{
    protected override void Execute(EditorActionContext context)
        => context.interactions.selection.Clear();
}
