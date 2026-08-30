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
    protected override EditorActionState Query(EditorActionContext context)
        => playMode.state switch
        {
            EditorPlayModeState.Editing => new EditorActionState(true, true, displayName: "Enter Play Mode"),
            EditorPlayModeState.EnteringPlay => new EditorActionState(
                true,
                true,
                isChecked: true,
                displayName: "Cancel Play Mode Entry"),
            EditorPlayModeState.Playing => new EditorActionState(
                true,
                true,
                isChecked: true,
                displayName: "Return to Edit Mode"),
            EditorPlayModeState.ExitingPlay => new EditorActionState(
                true,
                false,
                isChecked: true,
                displayName: "Restoring Edit Mode"),
            _ => EditorActionState.hidden
        };

    protected override void Execute(EditorActionContext context)
    {
        if (playMode.state is EditorPlayModeState.EnteringPlay or EditorPlayModeState.Playing)
            _ = playMode.ExitPlayMode();
        else
            _ = playMode.EnterPlayMode();
    }
}
