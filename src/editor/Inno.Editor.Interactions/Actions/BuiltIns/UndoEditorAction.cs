using Inno.Core.Input;

namespace Inno.Editor.Interactions;

[EditorAction(EditorActions.Undo, priority: 1000)]
[EditorMenu(EditorAreas.MainMenu, "Edit/Undo", order: 100)]
[EditorShortcut(KeyCode.Z, primary: true)]
internal sealed class UndoEditorAction : EditorAction
{
    protected override EditorActionState Query(EditorActionContext context)
        => new(
            isVisible: true,
            isEnabled: context.history.canUndo,
            displayName: context.history.undoName is { } name ? $"Undo {name}" : "Undo");

    protected override void Execute(EditorActionContext context)
    {
        _ = context.history.Undo();
    }
}
