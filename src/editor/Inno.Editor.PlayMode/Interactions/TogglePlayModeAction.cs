using Inno.Core.Input;
using Inno.Editor.Interactions;

namespace Inno.Editor.PlayMode;

[EditorAction(PlayModeInteractionIds.C_TOGGLE_PLAY_MODE, PlayModeInteractionIds.C_MAIN_MENU_AREA)]
[EditorToolbarItem(
    PlayModeInteractionIds.C_MAIN_MENU_AREA,
    EditorToolbarIcon.Play,
    "Enter Play Mode",
    activeIcon: EditorToolbarIcon.Stop)]
[EditorShortcut(KeyCode.P, primary: true)]
internal sealed class TogglePlayModeAction(IEditorPlayMode playMode) : EditorAction
{
    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext context)
        => playMode.state switch
        {
            EditorPlayModeState.Editing => new EditorActionState(true, true, displayName: "Enter Play Mode"),
            EditorPlayModeState.Compiling => new EditorActionState(
                true,
                true,
                isChecked: true,
                displayName: "Cancel Script Compilation Wait"),
            EditorPlayModeState.Preparing => new EditorActionState(
                true,
                false,
                isChecked: true,
                displayName: "Preparing Play Mode"),
            EditorPlayModeState.Playing => new EditorActionState(
                true,
                true,
                isChecked: true,
                displayName: "Return to Edit Mode"),
            EditorPlayModeState.Stopping => new EditorActionState(
                true,
                false,
                isChecked: true,
                displayName: "Restoring Edit Mode"),
            EditorPlayModeState.Failed => new EditorActionState(
                true,
                true,
                displayName: "Retry Play Mode"),
            _ => EditorActionState.hidden
        };

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext context)
    {
        if (playMode.state is EditorPlayModeState.Compiling or EditorPlayModeState.Preparing or EditorPlayModeState.Playing)
            _ = playMode.ExitPlayMode();
        else
            _ = playMode.EnterPlayMode();
    }
}
