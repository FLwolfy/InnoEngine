using System;
using System.Collections.Generic;

using Inno.Core.Logging;
using Inno.Core.Input;
using Inno.Editor.Interactions;
using Inno.Editor.Panel.FileBrowser;
using Inno.Editor.Scene;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_SAVE)]
[EditorMenu(HierarchyInteractionIds.C_MAIN_MENU_AREA, "File/Save", order: 100)]
[EditorShortcut(KeyCode.S, primary: true)]
internal sealed class SaveCommand(
    IEditorSceneWorkspace workspace,
    AssetEditorModule assets) : EditorAction
{
    protected override EditorActionState Query(EditorActionContext context)
        => SceneManager.hasActiveScene ? EditorActionState.enabled : EditorActionState.disabled;

    protected override void Execute(EditorActionContext context)
    {
        try
        {
            IReadOnlyList<GameScene> scenes = SceneManager.loadedScenes;
            for (int i = 0; i < scenes.Count; i++)
            {
                _ = workspace.Save(
                    scenes[i],
                    assets.browser.currentDirectory);
            }
        }
        catch (Exception exception)
        {
            Log.Error("Failed to save open scenes: {0}", exception);
        }
    }
}
