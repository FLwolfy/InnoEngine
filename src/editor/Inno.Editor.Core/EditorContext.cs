using System;
using System.Collections.Generic;

using Inno.Core.Logging;
using Inno.Engine.Scene;

namespace Inno.Editor.Core;

/// <summary>
/// Shared runtime context used by all editor panels.
/// </summary>
public sealed class EditorContext
{
    private GameScene? m_ownedScene;

    /// <summary>
    /// Gets the shared selection state.
    /// </summary>
    public EditorSelectionState selection { get; } = new();

    /// <summary>
    /// Gets the in-memory log buffer backing the log panel.
    /// </summary>
    public EditorLogBuffer logs { get; } = new();

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
            _ = SceneManager.UnloadScene(m_ownedScene);
        }

        m_ownedScene = null;
        LogManager.UnregisterSink(logs);
    }
}
