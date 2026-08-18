using System;
using System.Collections.Generic;

using Inno.Core.Logging;
using Inno.Editor.HotKeys;
using Inno.Engine.Scene;

namespace Inno.Editor.Core;

/// <summary>
/// Shared runtime context used by all editor panels.
/// </summary>
public sealed class EditorContext
{
    private GameScene? m_ownedScene;

    /// <summary>
    /// Creates the shared editor context.
    /// </summary>
    /// <param name="hotKeys">Editor shortcut map.</param>
    /// <param name="sceneWorkspace">Scene document workspace.</param>
    public EditorContext(EditorHotKeyMap hotKeys, EditorSceneWorkspace sceneWorkspace)
    {
        this.hotKeys = hotKeys ?? throw new ArgumentNullException(nameof(hotKeys));
        this.sceneWorkspace = sceneWorkspace ?? throw new ArgumentNullException(nameof(sceneWorkspace));
    }

    /// <summary>
    /// Gets the shared selection state.
    /// </summary>
    public EditorSelectionState selection { get; } = new();

    /// <summary>
    /// Gets the in-memory log buffer backing the log panel.
    /// </summary>
    public EditorLogBuffer logs { get; } = new();

    /// <summary>
    /// Gets the centralized editor shortcut map.
    /// </summary>
    public EditorHotKeyMap hotKeys { get; }

    /// <summary>
    /// Gets the scene document and persistence workspace.
    /// </summary>
    public EditorSceneWorkspace sceneWorkspace { get; }

    /// <summary>
    /// Gets the active scene being edited.
    /// </summary>
    public GameScene scene => SceneManager.activeScene
        ?? throw new InvalidOperationException("The editor does not have an active scene.");

    /// <summary>
    /// Gets all scenes currently available to editor panels.
    /// </summary>
    public IReadOnlyList<GameScene> scenes => SceneManager.loadedScenes;

    /// <summary>
    /// Gets or sets the latest frame delta in seconds.
    /// </summary>
    public float frameDeltaTime { get; set; }

    /// <summary>
    /// Gets or sets the latest absolute runtime in seconds.
    /// </summary>
    public float totalTime { get; set; }

    /// <summary>
    /// Registers editor-wide services into global systems.
    /// </summary>
    public void Attach()
    {
        LogManager.RegisterSink(logs);
        sceneWorkspace.Attach();
        if (!SceneManager.hasActiveScene)
        {
            m_ownedScene = SceneManager.LoadNewScene();
        }
    }

    /// <summary>
    /// Unregisters editor-wide services from global systems.
    /// </summary>
    public void Detach()
    {
        selection.Clear();
        if (m_ownedScene is not null)
        {
            SceneManager.UnloadAllScenes();
        }

        m_ownedScene = null;
        sceneWorkspace.Detach();
        LogManager.UnregisterSink(logs);
    }
}
