using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Global;

[EditorAction("editor/clear-selection")]
internal sealed class ClearEditorSelectionAction : EditorAction
{
    protected override void Execute(EditorActionContext context)
        => context.interactions.SetSelection(null);
}

[EditorAction("editor/select")]
internal sealed class SelectEditorTargetAction : EditorAction<object>
{
    protected override void Execute(EditorActionContext<object> context)
        => context.interactions.SetSelection(context.target);
}
