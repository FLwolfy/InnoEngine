using System;
using System.Collections.Generic;

using Inno.Core.Logging;
using Inno.Extensibility.Modules;
using Inno.Scripting.Api;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Scene;
using Inno.Editor.Scripting;
using Inno.Scripting.Compiler;
using Inno.Runtime;

namespace Inno.Editor.PlayMode;

/// <summary>
/// Implements the deterministic state machine for one editor Play Mode workflow.
/// </summary>
/// <remarks>
/// The editor host advances transitions at frame-safe points. Editable scenes remain owned by the
/// scene workspace while the controller owns only the isolated runtime session.
/// </remarks>
public sealed class EditorPlayModeController :
    IEditorPlayMode,
    IEditorReloadParticipant,
    IDisposable
{
    private readonly IEditorHistoryIsolation m_history;
    private readonly EngineHost m_engineHost;
    private readonly Logger m_log;
    private readonly RuntimeSessionOptions m_runtimeOptions;
    private readonly IEditorScenePlayMode m_scenes;
    private readonly IEditorScriptCompilation m_scripting;

    private IDisposable? m_historyIsolation;
    private IDisposable? m_sceneLease;
    private IScriptCompilationTicket? m_compilationTicket;
    private RuntimeSession? m_runtimeSession;
    private EditorPlayModeState m_state;
    private float m_playTime;
    private string? m_lastFailure;
    private LogSessionId m_activeSessionId;
    private bool m_disposed;

    /// <summary>
    /// Creates a Play Mode controller around the runtime host, scene snapshot, scripting, and history boundaries.
    /// </summary>
    /// <param name="engineHost">
    /// The application host that owns every isolated Play Mode runtime session.
    /// </param>
    /// <param name="runtimeOptions">
    /// The validated Play Mode storage and timing policy used for every accepted request.
    /// </param>
    /// <param name="scenes">
    /// The scene workspace that creates isolated runtime scene sessions.
    /// </param>
    /// <param name="scripting">
    /// The script compilation service that publishes generation readiness.
    /// </param>
    /// <param name="history">
    /// The editor history boundary isolated during simulation.
    /// </param>
    /// <param name="logs">
    /// The application log router that receives Play Mode lifecycle failures.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any dependency is <see langword="null"/>.
    /// </exception>
    public EditorPlayModeController(
        EngineHost engineHost,
        RuntimeSessionOptions runtimeOptions,
        IEditorScenePlayMode scenes,
        IEditorScriptCompilation scripting,
        IEditorHistoryIsolation history,
        LogRouter logs)
    {
        m_engineHost = engineHost ?? throw new ArgumentNullException(nameof(engineHost));
        m_runtimeOptions = runtimeOptions ?? throw new ArgumentNullException(nameof(runtimeOptions));
        if (runtimeOptions.kind != RuntimeSessionKind.Play)
        {
            throw new ArgumentException(
                "Editor Play Mode requires RuntimeSessionKind.Play options.",
                nameof(runtimeOptions));
        }
        m_scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
        m_scripting = scripting ?? throw new ArgumentNullException(nameof(scripting));
        m_history = history ?? throw new ArgumentNullException(nameof(history));
        ArgumentNullException.ThrowIfNull(logs);
        m_log = logs.CreateLogger<EditorPlayModeController>();
    }

    /// <summary>
    /// Gets the current Play Mode transition state.
    /// </summary>
    public EditorPlayModeState state => m_state;

    /// <summary>
    /// Gets whether isolated runtime scenes are actively simulating.
    /// </summary>
    public bool isPlaying => m_state == EditorPlayModeState.Playing;

    /// <summary>
    /// Gets the most recent transition or simulation failure.
    /// </summary>
    public string? lastFailure => m_lastFailure;

    /// <summary>
    /// Gets the isolated runtime log session associated with the current request.
    /// </summary>
    public LogSessionId activeSessionId => m_activeSessionId;

    /// <summary>
    /// Occurs after the controller commits a state transition.
    /// </summary>
    public event Action<EditorPlayModeState>? stateChanged;

    /// <summary>
    /// Requests a fresh script generation before preparing an isolated runtime session.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the controller accepted a new request; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the controller has been disposed.
    /// </exception>
    public bool EnterPlayMode()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_state is not (EditorPlayModeState.Editing or EditorPlayModeState.Failed))
            return false;
        m_lastFailure = null;
        m_activeSessionId = LogSessionId.none;
        m_compilationTicket = m_scripting.RequestCompilation();
        SetState(EditorPlayModeState.Compiling);
        return true;
    }

    /// <summary>
    /// Requests cancellation, runtime-session disposal, or dismissal of a failed transition.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the current state accepted the request; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the controller has been disposed.
    /// </exception>
    public bool ExitPlayMode()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        switch (m_state)
        {
            case EditorPlayModeState.Compiling:
                CompleteEditingTransition();
                return true;
            case EditorPlayModeState.Preparing:
            case EditorPlayModeState.Playing:
                SetState(EditorPlayModeState.Stopping);
                return true;
            case EditorPlayModeState.Failed:
                CompleteEditingTransition();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Advances at most one Play Mode transition at an editor-controlled frame-safe point.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the controller has been disposed.
    /// </exception>
    [ScriptingApiIgnore]
    public void AdvanceTransition()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        switch (m_state)
        {
            case EditorPlayModeState.Compiling:
                AdvanceCompilation();
                break;
            case EditorPlayModeState.Preparing:
                PrepareRuntimeSession();
                break;
            case EditorPlayModeState.Stopping:
                StopRuntimeSession();
                break;
        }
    }

    /// <summary>
    /// Advances the isolated runtime session by one complete frame.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed editor frame time in seconds.
    /// </param>
    [ScriptingApiIgnore]
    public void Tick(float deltaTime)
        => RunSimulation(
            session =>
            {
                m_playTime += Math.Max(0f, deltaTime);
                session.Tick(m_playTime, deltaTime);
            },
            "frame update");

    /// <summary>
    /// Releases the isolated runtime session and history scope owned by this controller.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        try
        {
            ReleasePlayResources();
            if (m_state != EditorPlayModeState.Editing)
                CompleteEditingTransition();
        }
        finally
        {
            m_disposed = true;
        }
    }

    /// <summary>
    /// Captures the Play Mode quiescence operation required before an assembly generation switch.
    /// </summary>
    /// <param name="context">
    /// The prepared assembly reload context whose candidate will replace the active generation.
    /// </param>
    /// <returns>
    /// A transaction that synchronously releases the transient Play session before candidate activation.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after the controller has been disposed.
    /// </exception>
    IEditorReloadTransaction IEditorReloadParticipant.Capture(AssemblyReloadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        return new PlayModeReloadTransaction(this);
    }

    /// <summary>
    /// Republishes diagnostics owned by Play Mode after an assembly generation transition.
    /// </summary>
    void IEditorReloadParticipant.RefreshDiagnostics()
    {
    }

    private void AdvanceCompilation()
    {
        IScriptCompilationTicket? ticket = m_compilationTicket;
        if (ticket is null)
        {
            Fail("Play Mode lost its script compilation ticket.");
            return;
        }
        switch (ticket.state)
        {
            case ScriptCompilationTicketState.Queued:
            case ScriptCompilationTicketState.Compiling:
                return;
            case ScriptCompilationTicketState.Succeeded:
                SetState(EditorPlayModeState.Preparing);
                return;
            case ScriptCompilationTicketState.Failed:
                Fail(CreateCompilationFailure(ticket));
                return;
            case ScriptCompilationTicketState.Canceled:
            case ScriptCompilationTicketState.Superseded:
                Fail($"Play Mode did not start because its script request was {ticket.state.ToString().ToLowerInvariant()}. {ticket.status}");
                return;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void PrepareRuntimeSession()
    {
        IDisposable? historyIsolation = null;
        RuntimeSession? runtimeSession = null;
        IDisposable? sceneLease = null;
        try
        {
            historyIsolation = m_history.BeginHistoryIsolation();
            runtimeSession = m_engineHost.CreateSession(m_runtimeOptions);
            using (runtimeSession.EnterExecutionScope())
                sceneLease = m_scenes.BeginPlayMode(runtimeSession);
            m_historyIsolation = historyIsolation;
            m_runtimeSession = runtimeSession;
            m_sceneLease = sceneLease;
            m_activeSessionId = runtimeSession.sessionId;
            m_playTime = 0f;
            SetState(EditorPlayModeState.Playing);
        }
        catch (Exception exception)
        {
            sceneLease?.Dispose();
            runtimeSession?.Dispose();
            historyIsolation?.Dispose();
            m_log.Write(LogLevel.Error, "Play Mode entry failed: {0}", [exception]);
            Fail($"Play Mode could not start: {exception.Message}");
        }
    }

    private void StopRuntimeSession()
    {
        try
        {
            ReleasePlayResources();
            CompleteEditingTransition();
        }
        catch (Exception exception)
        {
            m_lastFailure = $"Edit Mode could not be restored: {exception.Message}";
            m_log.Write(LogLevel.Error, "Play Mode exit failed and will be retried: {0}", [exception]);
        }
    }

    private void ReleasePlayResources()
    {
        List<Exception>? failures = null;
        DisposeResource(ref m_sceneLease, ref failures);
        DisposeResource(ref m_runtimeSession, ref failures);
        DisposeResource(ref m_historyIsolation, ref failures);
        if (failures is not null)
            throw new AggregateException("Play Mode resource disposal failed.", failures);
    }

    private void QuiesceForAssemblyReload()
    {
        List<Exception>? failures = null;
        try
        {
            ReleasePlayResources();
        }
        catch (Exception exception)
        {
            failures = [exception];
            m_lastFailure = $"Play Mode could not release its runtime generation: {exception.Message}";
        }
        try
        {
            CompleteEditingTransition();
        }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }

        if (failures is not null)
        {
            throw new InvalidOperationException(
                "Assembly reload cannot continue because Play Mode still owns generation-bound runtime state.",
                new AggregateException(failures));
        }
    }

    private void RunSimulation(Action<RuntimeSession> callback, string phase)
    {
        if (m_state != EditorPlayModeState.Playing)
            return;
        RuntimeSession session = m_runtimeSession
            ?? throw new InvalidOperationException("Playing state has no active runtime session.");
        try
        {
            callback(session);
        }
        catch (Exception exception)
        {
            m_lastFailure = $"Play Mode {phase} failed: {exception.Message}";
            m_log.Write(
                LogLevel.Error,
                "Play Mode {0} failed; isolated runtime disposal was requested: {1}",
                [phase, exception]);
            SetState(EditorPlayModeState.Stopping);
        }
    }

    internal IDisposable? EnterPresentationScope()
        => m_state == EditorPlayModeState.Playing
            ? m_runtimeSession?.EnterExecutionScope()
            : null;

    private void Fail(string failure)
    {
        m_lastFailure = failure;
        ReleasePlayResources();
        SetState(EditorPlayModeState.Failed);
    }

    private void CompleteEditingTransition()
    {
        m_compilationTicket = null;
        m_activeSessionId = LogSessionId.none;
        m_playTime = 0f;
        SetState(EditorPlayModeState.Editing);
    }

    private static string CreateCompilationFailure(IScriptCompilationTicket ticket)
    {
        ScriptCompilationResult? compilation = ticket.result;
        if (compilation is null)
            return $"Play Mode requires a valid script generation. {ticket.status}";
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
        List<Exception>? failures = null;
        foreach (Action<EditorPlayModeState> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(value);
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }
        if (failures is not null)
            throw new AggregateException("One or more Play Mode state observers failed.", failures);
    }

    private static void DisposeResource<T>(ref T? resource, ref List<Exception>? failures)
        where T : class, IDisposable
    {
        T? owned = resource;
        resource = null;
        if (owned is null)
            return;
        try
        {
            owned.Dispose();
        }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }
    }

    private sealed class PlayModeReloadTransaction(EditorPlayModeController owner)
        : IEditorReloadTransaction
    {
        private bool m_prepared;

        /// <summary>
        /// Releases the isolated Play world before the candidate generation becomes active.
        /// </summary>
        public void PrepareForActivation()
        {
            if (m_prepared)
                return;
            owner.QuiesceForAssemblyReload();
            m_prepared = true;
        }

        /// <summary>
        /// Completes the state transition already committed during preparation.
        /// </summary>
        public void Apply()
        {
        }

        /// <summary>
        /// Releases transaction state after a successful assembly generation switch.
        /// </summary>
        public void Complete()
        {
        }

        /// <summary>
        /// Preserves Edit Mode when candidate activation is rolled back.
        /// </summary>
        public void RollbackStructure()
        {
        }

        /// <summary>
        /// Preserves Edit Mode after the previous assembly generation is restored.
        /// </summary>
        /// <remarks>
        /// A running simulation is transient state and is intentionally never reconstructed after
        /// an assembly reload request has quiesced it.
        /// </remarks>
        public void RestorePreviousState()
        {
        }
    }
}
