using System;

using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Editor.Scripting;
using Inno.Runtime;

namespace Inno.Editor.PlayMode;

[EditorModule("play-mode", order: 220)]
internal sealed class EditorPlayModeModule : EditorModule, IEditorPlayMode
{
    private readonly EditorPlayModeController m_controller;
    private readonly EditorPlayModeLoop m_loop;
    private readonly EditorReloadCoordinator m_reloads;

    private IDisposable? m_reloadRegistration;

    internal EditorPlayModeModule(
        EditorPlayModeLoop loop,
        EngineHost engineHost,
        RuntimeSessionOptions runtimeOptions,
        IEditorScenePlayMode scenes,
        IEditorScriptCompilation scripting,
        EditorInteractions interactions,
        EditorReloadCoordinator reloads)
    {
        m_loop = loop ?? throw new ArgumentNullException(nameof(loop));
        m_reloads = reloads ?? throw new ArgumentNullException(nameof(reloads));
        m_controller = new EditorPlayModeController(
            engineHost,
            runtimeOptions,
            scenes,
            scripting,
            interactions,
            engineHost.logs);
    }

    /// <summary>
    /// Gets whether the current transition must prevent lower-priority editor updates.
    /// </summary>
    public override bool blocksFollowingUpdates
        => state is EditorPlayModeState.Compiling or EditorPlayModeState.Preparing or EditorPlayModeState.Stopping;

    /// <summary>
    /// Gets the current lifecycle state observed by callers.
    /// </summary>
    public EditorPlayModeState state => m_controller.state;

    /// <summary>
    /// Gets whether an isolated runtime session is actively simulating.
    /// </summary>
    public bool isPlaying => m_controller.isPlaying;

    /// <summary>
    /// Gets the most recent transition failure, or <see langword="null"/> when no failure is active.
    /// </summary>
    public string? lastFailure => m_controller.lastFailure;

    /// <summary>
    /// Gets the isolated log session that owns the current Play Mode request.
    /// </summary>
    public LogSessionId activeSessionId => m_controller.activeSessionId;

    /// <summary>
    /// Occurs after the Play Mode transition state changes.
    /// </summary>
    public event Action<EditorPlayModeState>? stateChanged
    {
        add => m_controller.stateChanged += value;
        remove => m_controller.stateChanged -= value;
    }

    /// <summary>
    /// Requests entry after the active script generation becomes ready.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a new entry request was accepted; otherwise, <see langword="false"/>.
    /// </returns>
    public bool EnterPlayMode() => m_controller.EnterPlayMode();

    /// <summary>
    /// Requests disposal of the active runtime session or dismisses a failed transition.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when entry was cancelled or a new exit request was accepted; otherwise, <see langword="false"/>.
    /// </returns>
    public bool ExitPlayMode() => m_controller.ExitPlayMode();

    /// <summary>
    /// Initializes this feature after its owning runtime has activated all required services.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void OnStart(EditorContext context)
    {
        m_reloadRegistration = m_reloads.Register(m_controller);
        try
        {
            m_loop.Attach(m_controller);
        }
        catch
        {
            m_reloadRegistration.Dispose();
            m_reloadRegistration = null;
            throw;
        }
    }

    /// <summary>
    /// Advances this feature once using the current editor or runtime frame state.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void OnUpdate(EditorContext context)
        => m_controller.AdvanceTransition();

    /// <summary>
    /// Stops this feature before its owning runtime releases generation-scoped services.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void OnStop(EditorContext context)
    {
        try
        {
            m_loop.Detach(m_controller);
            m_controller.Dispose();
        }
        finally
        {
            m_reloadRegistration?.Dispose();
            m_reloadRegistration = null;
        }
    }

    /// <summary>
    /// Releases resources retained by this feature after it has stopped.
    /// </summary>
    protected override void OnDispose()
    {
        m_reloadRegistration?.Dispose();
        m_reloadRegistration = null;
        m_controller.Dispose();
    }
}
