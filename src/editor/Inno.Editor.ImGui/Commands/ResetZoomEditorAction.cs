using Inno.Core.Input;
using Inno.Editor.Interactions;

namespace Inno.Editor.ImGui;

/// <summary>
/// Restores the global editor UI zoom to its baseline multiplier.
/// </summary>
[EditorAction(EditorZoomActions.Reset)]
[EditorMenu(EditorAreas.MainMenu, "View/Actual Size", order: 120)]
[EditorShortcut(KeyCode.D0, primary: true)]
internal sealed class ResetZoomEditorAction(EditorZoomModule zoom) : EditorAction
{
    /// <inheritdoc />
    protected override EditorActionState Query(EditorActionContext context)
        => System.MathF.Abs(zoom.zoom - 1f) > 0.0001f
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    /// <inheritdoc />
    protected override void Execute(EditorActionContext context)
        => _ = zoom.Reset();
}
