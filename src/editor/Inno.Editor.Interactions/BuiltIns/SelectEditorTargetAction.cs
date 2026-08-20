using Inno.Editor.Interactions.Actions;

namespace Inno.Editor.Interactions.BuiltIns;

[EditorAction(EditorActions.Select)]
internal sealed class SelectEditorTargetAction : EditorAction<object>
{
    protected override void Execute(EditorActionContext<object> context)
        => context.interactions.selection.Select(context.target);
}
