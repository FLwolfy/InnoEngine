using Inno.Core.Input;

namespace Inno.Editor.Interactions;

[EditorAction(EditorActions.Redo, priority: 1000)]
[EditorMenu(EditorAreas.MainMenu, "Edit/Redo", order: 110)]
[EditorShortcut(KeyCode.Z, KeyModifier.Shift, primary: true)]
[EditorShortcut(KeyCode.Y, primary: true)]
internal sealed class RedoEditorAction : EditorAction
{
    protected override EditorActionState Query(EditorActionContext context)
        => new(
            isVisible: true,
            isEnabled: context.history.canRedo,
            displayName: context.history.redoName is { } name ? $"Redo {name}" : "Redo");

    protected override void Execute(EditorActionContext context)
    {
        _ = context.history.Redo();
    }
}
