using Inno.Core.Input;
using Inno.Editor.Interactions;

namespace Inno.Editor.ImGui;

/// <summary>
/// Increases the global editor UI zoom by one bounded increment.
/// </summary>
[EditorAction(EditorZoomActions.ZoomIn)]
[EditorMenu(EditorAreas.MainMenu, "View/Zoom In", order: 100)]
[EditorShortcut(KeyCode.Plus, primary: true)]
internal sealed class ZoomInEditorAction(EditorZoomModule zoom) : EditorAction
{
    /// <inheritdoc />
    protected override EditorActionState Query(EditorActionContext context)
        => zoom.zoom < EditorStyleMetrics.C_MAX_ZOOM
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    /// <inheritdoc />
    protected override void Execute(EditorActionContext context)
        => _ = zoom.ZoomIn();
}
