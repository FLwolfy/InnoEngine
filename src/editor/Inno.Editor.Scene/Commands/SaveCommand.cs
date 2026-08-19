using Inno.Editor.Assets;
using System;
using System.Collections.Generic;

using Inno.Core.Logging;
using Inno.Core.Input;
using Inno.Editor.Core.Commands;
using Inno.Editor.Scene.Workspace;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene.Commands;

[EditorAction(EditorActionIds.Save)]
[EditorShortcut(KeyCode.S, primary: true)]
internal sealed class SaveCommand(
    EditorSceneWorkspace workspace,
    AssetEditorModule assets) : EditorAction
{
    public override EditorActionState Query(EditorActionContext context)
        => SceneManager.hasActiveScene ? EditorActionState.enabled : EditorActionState.disabled;

    public override void Execute(EditorActionContext context)
    {
        try
        {
            IReadOnlyList<GameScene> scenes = SceneManager.loadedScenes;
            for (int i = 0; i < scenes.Count; i++)
            {
                _ = workspace.SaveScene(
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
