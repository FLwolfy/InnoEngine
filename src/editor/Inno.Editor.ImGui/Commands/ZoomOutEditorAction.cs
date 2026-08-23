using Inno.Core.Input;
using Inno.Editor.Interactions;

namespace Inno.Editor.ImGui;

/// <summary>
/// Decreases the global editor UI zoom by one bounded increment.
/// </summary>
[EditorAction(EditorZoomActions.ZoomOut)]
[EditorMenu(EditorAreas.MainMenu, "View/Zoom Out", order: 110)]
[EditorShortcut(KeyCode.Minus, primary: true)]
internal sealed class ZoomOutEditorAction(EditorZoomModule zoom) : EditorAction
{
    /// <inheritdoc />
    protected override EditorActionState Query(EditorActionContext context)
        => zoom.zoom > EditorStyleMetrics.C_MIN_ZOOM
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    /// <inheritdoc />
    protected override void Execute(EditorActionContext context)
        => _ = zoom.ZoomOut();
}
