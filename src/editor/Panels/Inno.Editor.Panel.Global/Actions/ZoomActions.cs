using Inno.Core.Input;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Global;

/// <summary>
/// Increases UI zoom by one multiple of the configured actual size.
/// </summary>
[EditorAction("editor.ui.zoom-in")]
[EditorMenu("editor/main-menu", "View/Zoom In", order: 100)]
[EditorShortcut(KeyCode.Plus, primary: true)]
internal sealed class ZoomInEditorAction(EditorZoomModule zoom) : EditorAction
{
    /// <inheritdoc />
    protected override EditorActionState Query(EditorActionContext context)
        => zoom.canZoomIn
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    /// <inheritdoc />
    protected override void Execute(EditorActionContext context)
        => _ = zoom.ZoomIn();
}

/// <summary>
/// Decreases UI zoom by one multiple of the configured actual size.
/// </summary>
[EditorAction("editor.ui.zoom-out")]
[EditorMenu("editor/main-menu", "View/Zoom Out", order: 110)]
[EditorShortcut(KeyCode.Minus, primary: true)]
internal sealed class ZoomOutEditorAction(EditorZoomModule zoom) : EditorAction
{
    /// <inheritdoc />
    protected override EditorActionState Query(EditorActionContext context)
        => zoom.canZoomOut
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    /// <inheritdoc />
    protected override void Execute(EditorActionContext context)
        => _ = zoom.ZoomOut();
}

/// <summary>
/// Restores UI zoom to the configured actual size.
/// </summary>
[EditorAction("editor.ui.zoom-reset")]
[EditorMenu("editor/main-menu", "View/Actual Size", order: 120)]
[EditorShortcut(KeyCode.D0, primary: true)]
internal sealed class ActualSizeEditorAction(EditorZoomModule zoom) : EditorAction
{
    /// <inheritdoc />
    protected override EditorActionState Query(EditorActionContext context)
        => zoom.isActualSize
            ? EditorActionState.disabled
            : EditorActionState.enabled;

    /// <inheritdoc />
    protected override void Execute(EditorActionContext context)
        => _ = zoom.UseActualSize();
}
