using System;

using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Editor.Scripting;
using Inno.Engine.Scene;

namespace Inno.Editor.PlayMode;

/// <summary>Coordinates script readiness, scene isolation, history isolation, and game simulation.</summary>
[EditorModule("play-mode", order: 220)]
internal sealed class EditorPlayModeModule : EditorModule, IEditorPlayMode
{
    private readonly EditorInteractions m_interactions;
    private readonly EditorPlayModeLoop m_loop;
    private readonly IEditorScenePlayMode m_scenes;
    private readonly IEditorScriptCompilation m_scripting;

    private IDisposable? m_historyIsolation;
    private IEditorScenePlayModeSession? m_sceneSession;
    private EditorPlayModeState m_state;
    private string? m_lastFailure;

    internal EditorPlayModeModule(
        EditorPlayModeLoop loop,
        IEditorScenePlayMode scenes,
        IEditorScriptCompilation scripting,
        EditorInteractions interactions)
    {
        m_loop = loop ?? throw new ArgumentNullException(nameof(loop));
        m_scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
        m_scripting = scripting ?? throw new ArgumentNullException(nameof(scripting));
        m_interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    }

    /// <inheritdoc />
    public override bool blocksFollowingUpdates
        => m_state is EditorPlayModeState.EnteringPlay or EditorPlayModeState.ExitingPlay;

    /// <inheritdoc />
    public EditorPlayModeState state => m_state;

    /// <inheritdoc />
    public bool isPlaying => m_state == EditorPlayModeState.Playing;

    /// <inheritdoc />
    public string? lastFailure => m_lastFailure;

    /// <inheritdoc />
    public event Action<EditorPlayModeState>? stateChanged;

    /// <inheritdoc />
    public bool EnterPlayMode()
    {
        if (m_state != EditorPlayModeState.Editing)
            return false;
        m_lastFailure = null;
        SetState(EditorPlayModeState.EnteringPlay);
        return true;
    }

    /// <inheritdoc />
    public bool ExitPlayMode()
    {
        switch (m_state)
        {
            case EditorPlayModeState.EnteringPlay when m_sceneSession is null:
                SetState(EditorPlayModeState.Editing);
                return true;
            case EditorPlayModeState.Playing:
                SetState(EditorPlayModeState.ExitingPlay);
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    protected override void OnStart(EditorContext context) => m_loop.Attach(this);

    /// <inheritdoc />
    protected override void OnUpdate(EditorContext context)
    {
        if (m_state == EditorPlayModeState.EnteringPlay)
            TryEnterPlayMode();
        else if (m_state == EditorPlayModeState.ExitingPlay)
            TryExitPlayMode();
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        try
        {
            RestoreEditingState();
        }
        finally
        {
            m_loop.Detach(this);
            SetState(EditorPlayModeState.Editing);
        }
    }

    internal void FixedUpdate(float fixedDeltaTime)
        => RunSimulation(() => SceneManager.FixedUpdate(fixedDeltaTime), "fixed update");

    internal void UpdateSimulation(float deltaTime)
        => RunSimulation(() => SceneManager.Update(deltaTime), "update");

    internal void LateUpdate(float deltaTime)
        => RunSimulation(() => SceneManager.LateUpdate(deltaTime), "late update");

    private void TryEnterPlayMode()
    {
        switch (m_scripting.state)
        {
            case EditorScriptCompilationState.Initializing:
            case EditorScriptCompilationState.Compiling:
                return;
            case EditorScriptCompilationState.Failed:
                m_lastFailure = CreateCompilationFailure();
                SetState(EditorPlayModeState.Editing);
                return;
            case EditorScriptCompilationState.Ready:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        IDisposable? historyIsolation = null;
        try
        {
            historyIsolation = m_interactions.BeginHistoryIsolation();
            IEditorScenePlayModeSession sceneSession = m_scenes.BeginPlayMode();
            m_historyIsolation = historyIsolation;
            m_sceneSession = sceneSession;
            SetState(EditorPlayModeState.Playing);
        }
        catch (Exception exception)
        {
            historyIsolation?.Dispose();
            m_lastFailure = $"Play Mode could not start: {exception.Message}";
            Log.Error("Play Mode entry failed: {0}", exception);
            SetState(EditorPlayModeState.Editing);
        }
    }

    private void TryExitPlayMode()
    {
        try
        {
            RestoreEditingState();
            SetState(EditorPlayModeState.Editing);
        }
        catch (Exception exception)
        {
            m_lastFailure = $"Edit Mode could not be restored: {exception.Message}";
            Log.Error("Play Mode exit failed and will be retried: {0}", exception);
        }
    }

    private void RestoreEditingState()
    {
        m_sceneSession?.Restore();
        m_sceneSession = null;
        m_historyIsolation?.Dispose();
        m_historyIsolation = null;
    }

    private void RunSimulation(Action callback, string phase)
    {
        if (m_state != EditorPlayModeState.Playing)
            return;
        try
        {
            callback();
        }
        catch (Exception exception)
        {
            m_lastFailure = $"Play Mode {phase} failed: {exception.Message}";
            Log.Error("Play Mode {0} failed; Edit Mode restoration was requested: {1}", phase, exception);
            SetState(EditorPlayModeState.ExitingPlay);
        }
    }

    private string CreateCompilationFailure()
    {
        ScriptCompilationResult? compilation = m_scripting.lastCompilation;
        if (compilation is null)
            return $"Play Mode requires a valid script generation. {m_scripting.status}";
        for (int i = 0; i < compilation.diagnostics.Count; i++)
        {
            ScriptDiagnostic diagnostic = compilation.diagnostics[i];
            if (diagnostic.severity == ScriptDiagnosticSeverity.Error)
                return $"Play Mode is unavailable because scripts did not compile: {diagnostic.message}";
        }
        return "Play Mode is unavailable because the active script compilation failed.";
    }

    private void SetState(EditorPlayModeState value)
    {
        if (m_state == value)
            return;
        m_state = value;
        Action<EditorPlayModeState>? handlers = stateChanged;
        if (handlers is null)
            return;
        foreach (Action<EditorPlayModeState> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(value);
            }
            catch (Exception exception)
            {
                Log.Error("A Play Mode state subscriber failed: {0}", exception);
            }
        }
    }
}
