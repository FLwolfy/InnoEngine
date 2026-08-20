using System;
using System.Collections.Generic;

using Inno.Core.Logging;
using Inno.Core.Input;
using Inno.Editor.Interactions.Actions;
using Inno.Editor.Panel.Hierarchy.Workspace;
using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Hierarchy.Commands;

[EditorAction(EditorActions.Save)]
[EditorShortcut(KeyCode.S, primary: true)]
internal sealed class SaveCommand(EditorSceneWorkspace workspace) : EditorAction
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
                _ = workspace.SaveScene(
                    scenes[i],
                    string.Empty);
            }
        }
        catch (Exception exception)
        {
            Log.Error("Failed to save open scenes: {0}", exception);
        }
    }
}
