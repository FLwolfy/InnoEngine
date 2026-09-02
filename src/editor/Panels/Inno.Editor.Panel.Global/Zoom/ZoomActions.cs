using Inno.Core.Input;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Global;

/// <summary>
/// Increases UI zoom by one multiple of the configured actual size.
/// </summary>
/// <param name="zoom">
/// The zoom used to initialize this instance.
/// </param>
[EditorAction(GlobalInteractionIds.C_ZOOM_IN)]
[EditorMenu(GlobalInteractionIds.C_MAIN_MENU_AREA, "View/Zoom In", order: 100)]
[EditorShortcut(KeyCode.Plus, primary: true)]
internal sealed class ZoomInEditorAction(EditorZoomModule zoom) : EditorAction
{
    /// <summary>
    /// Evaluates the operation's current availability and presentation state.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext context)
        => zoom.canZoomIn
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    /// <summary>
    /// Applies the editor action to the supplied interaction context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void Execute(EditorActionContext context)
        => _ = zoom.ZoomIn();
}

/// <summary>
/// Decreases UI zoom by one multiple of the configured actual size.
/// </summary>
/// <param name="zoom">
/// The zoom used to initialize this instance.
/// </param>
[EditorAction(GlobalInteractionIds.C_ZOOM_OUT)]
[EditorMenu(GlobalInteractionIds.C_MAIN_MENU_AREA, "View/Zoom Out", order: 110)]
[EditorShortcut(KeyCode.Minus, primary: true)]
internal sealed class ZoomOutEditorAction(EditorZoomModule zoom) : EditorAction
{
    /// <summary>
    /// Evaluates the operation's current availability and presentation state.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext context)
        => zoom.canZoomOut
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    /// <summary>
    /// Applies the editor action to the supplied interaction context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void Execute(EditorActionContext context)
        => _ = zoom.ZoomOut();
}

/// <summary>
/// Restores UI zoom to the configured actual size.
/// </summary>
/// <param name="zoom">
/// The zoom used to initialize this instance.
/// </param>
[EditorAction(GlobalInteractionIds.C_ZOOM_RESET)]
[EditorMenu(GlobalInteractionIds.C_MAIN_MENU_AREA, "View/Actual Size", order: 120)]
[EditorShortcut(KeyCode.D0, primary: true)]
internal sealed class ActualSizeEditorAction(EditorZoomModule zoom) : EditorAction
{
    /// <summary>
    /// Evaluates the operation's current availability and presentation state.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext context)
        => zoom.isActualSize
            ? EditorActionState.disabled
            : EditorActionState.enabled;

    /// <summary>
    /// Applies the editor action to the supplied interaction context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void Execute(EditorActionContext context)
        => _ = zoom.UseActualSize();
}
