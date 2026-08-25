using Inno.Core.Input;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Global;

[EditorAction("editor/undo", priority: 1000)]
[EditorMenu("editor/main-menu", "Edit/Undo", order: 100)]
[EditorShortcut(KeyCode.Z, primary: true)]
internal sealed class UndoEditorAction : EditorAction
{
    protected override EditorActionState Query(EditorActionContext context)
        => new(
            isVisible: true,
            isEnabled: context.history.canUndo,
            displayName: context.history.undoName is { } name
                ? context.history.undoUnavailableReason is { } reason
                    ? $"Undo {name} ({reason})"
                    : $"Undo {name}"
                : "Undo");

    protected override void Execute(EditorActionContext context)
        => _ = context.history.Undo();
}

[EditorAction("editor/redo", priority: 1000)]
[EditorMenu("editor/main-menu", "Edit/Redo", order: 110)]
[EditorShortcut(KeyCode.Z, KeyModifier.Shift, primary: true)]
[EditorShortcut(KeyCode.Y, primary: true)]
internal sealed class RedoEditorAction : EditorAction
{
    protected override EditorActionState Query(EditorActionContext context)
        => new(
            isVisible: true,
            isEnabled: context.history.canRedo,
            displayName: context.history.redoName is { } name
                ? context.history.redoUnavailableReason is { } reason
                    ? $"Redo {name} ({reason})"
                    : $"Redo {name}"
                : "Redo");

    protected override void Execute(EditorActionContext context)
        => _ = context.history.Redo();
}
