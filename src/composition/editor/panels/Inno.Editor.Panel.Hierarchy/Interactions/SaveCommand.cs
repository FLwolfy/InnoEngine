using System;
using System.Collections.Generic;

using Inno.Core.Logging;
using Inno.Core.Input;
using Inno.Editor.Interactions;
using Inno.Editor.Panel.FileBrowser;
using Inno.Editor.Scene;
using Inno.Scene;

namespace Inno.Editor.Panel.Hierarchy;

[EditorAction(HierarchyInteractionIds.C_SAVE)]
[EditorMenu(HierarchyInteractionIds.C_MAIN_MENU_AREA, "File/Save", order: 100)]
[EditorShortcut(KeyCode.S, primary: true)]
internal sealed class SaveCommand(
    IEditorSceneWorkspace workspace,
    AssetEditorModule assets,
    LogRouter logs) : EditorAction
{
    private readonly Logger m_log = (logs ?? throw new ArgumentNullException(nameof(logs)))
        .CreateLogger<SaveCommand>();

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
        => workspace.canPersist && workspace.activeScene is not null
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext context)
    {
        try
        {
            IReadOnlyList<GameScene> scenes = workspace.scenes;
            for (int i = 0; i < scenes.Count; i++)
            {
                _ = workspace.Save(
                    scenes[i],
                    assets.browser.projectDirectory);
            }
        }
        catch (Exception exception)
        {
            m_log.Write(LogLevel.Error, "Failed to save open scenes: {0}", [exception]);
        }
    }
}
