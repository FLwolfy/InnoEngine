using Inno.Core.Diagnostics;
using Inno.Core.Execution;
using Inno.Extensibility.Reload;
using System;
using System.Collections.Generic;

using Inno.Core.Logging;
using Inno.Extensibility.Modules;
using Inno.Scripting.Api;
using Inno.Editor.Audio;
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
    private readonly IEditorAudioHost? m_audio;
    private readonly EngineHost m_engineHost;
    private readonly Logger m_log;
    private readonly RuntimeSessionOptions m_runtimeOptions;
    private readonly IEditorScenePlayMode m_scenes;
    private readonly IEditorScriptCompilation m_scripting;

    private IDisposable? m_historyIsolation;
    private IDisposable? m_sceneLease;
    private IDisposable? m_retirementReservation;
    private RetirementBarrier? m_retirement;
    private List<Exception>? m_retirementFailures;
    private bool m_entryFailed;
    private Exception? m_startupRetirementFailure;
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
    /// <param name="audio">
    /// Optional Editor audio host that creates an isolated backend generation for Play Mode.
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
        LogRouter logs,
        IEditorAudioHost? audio = null)
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
        m_audio = audio;
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
        m_engineHost.generations.EnsureReady("enter Play Mode");
        m_entryFailed = false;
        m_retirement = null;
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
                session.Tick(deltaTime);
            },
            "frame update");

    /// <summary>
    /// Releases the isolated runtime session and history scope owned by this controller.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        if (m_startupRetirementFailure is not null)
            throw m_startupRetirementFailure;
        try
        {
            if (m_runtimeSession is not null)
                SetState(EditorPlayModeState.Stopping);
            ReleasePlayResources();
            if (m_state != EditorPlayModeState.Editing)
                CompleteEditingTransition();
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception failure)
        {
            m_engineHost.generations.Fault(failure);
            throw;
        }
        m_disposed = true;
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
    IGenerationChange IEditorReloadParticipant.Capture(AssemblyReloadContext context)
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
        try
        {
            m_historyIsolation = m_history.BeginHistoryIsolation();
            m_runtimeSession = m_engineHost.CreateSession(m_runtimeOptions);
            using (m_runtimeSession.EnterExecutionScope())
                m_sceneLease = m_scenes.BeginPlayMode(m_runtimeSession);
            m_activeSessionId = m_runtimeSession.sessionId;
            m_playTime = 0f;
            SetState(EditorPlayModeState.Playing);
        }
        catch (Exception exception)
        {
            m_log.Write(LogLevel.Error, "Play Mode entry failed: {0}", [exception]);
            m_lastFailure = $"Play Mode could not start: {exception.Message}";
            m_entryFailed = true;
            SetState(EditorPlayModeState.Stopping);
            if (RetirementPendingException.Find(exception) is RetirementTimeoutException)
            {
                m_startupRetirementFailure = exception;
                throw;
            }
            StopRuntimeSession();
        }
    }

    private void StopRuntimeSession()
    {
        try
        {
            ReleasePlayResources();
            if (m_entryFailed)
                SetState(EditorPlayModeState.Failed);
            else
                CompleteEditingTransition();
        }
        catch (Exception failure) when (RetirementPendingException.Find(failure) is RetirementTimeoutException)
        {
            m_engineHost.generations.Fault(failure);
            m_lastFailure = failure.Message;
            throw;
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { }
        catch (Exception exception)
        {
            m_engineHost.generations.Fault(exception);
            m_lastFailure = $"Edit Mode could not be restored: {exception.Message}";
            m_log.Write(LogLevel.Error, "Play Mode retirement failed; the host must restart: {0}", [exception]);
            SetState(EditorPlayModeState.Failed);
        }
    }

    private void ReleasePlayResources()
    {
        if (m_startupRetirementFailure is not null)
            throw m_startupRetirementFailure;
        if (m_sceneLease is null && m_runtimeSession is null && m_historyIsolation is null)
            return;
        if (m_retirementReservation is null && m_engineHost.generations.state == GenerationState.Ready)
        {
            if (!m_engineHost.generations.TryAcquireChange("retire Play Mode", out m_retirementReservation))
                throw new RetirementPendingException("Play Mode retirement is waiting for the active generation reader.");
        }
        m_retirement ??= new RetirementBarrier("Editor Play Mode");
        if (!m_retirement.TryComplete(ReleasePlayResourcesCore))
            throw new RetirementPendingException("Play Mode is still draining its runtime session.");
        m_retirementReservation?.Dispose();
        m_retirementReservation = null;
    }

    private void ReleasePlayResourcesCore()
    {
        DisposeResource(ref m_sceneLease, ref m_retirementFailures);
        DisposeResource(ref m_runtimeSession, ref m_retirementFailures);
        DisposeResource(ref m_historyIsolation, ref m_retirementFailures);
        if (m_retirementFailures is not null)
        {
            var failure = new AggregateException("Play Mode resource disposal failed.", m_retirementFailures);
            m_retirementFailures = null;
            throw failure;
        }
    }

    internal void Quiesce()
    {
        if (m_disposed)
            return;
        SetState(EditorPlayModeState.Stopping);
        List<Exception>? failures = null;
        try
        {
            new RetirementBarrier("Play Mode before generation activation").Wait(ReleasePlayResources);
        }
        catch (Exception exception)
        {
            m_engineHost.generations.Fault(exception);
            if (RetirementPendingException.Find(exception) is not null)
                throw;
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
    {
        if (m_state != EditorPlayModeState.Playing || m_runtimeSession is not RuntimeSession session)
            return null;
        IDisposable? audioScope = null;
        try
        {
            audioScope = m_audio?.EnterExecutionScope(session);
            return new PresentationScope(session.EnterExecutionScope(), audioScope);
        }
        catch
        {
            audioScope?.Dispose();
            throw;
        }
    }

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
            if (diagnostic.severity == DiagnosticSeverity.Error)
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
        if (owned is null)
            return;
        try
        {
            owned.Dispose();
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }
        resource = null;
    }

    private sealed class PresentationScope(
        IDisposable sessionScope,
        IDisposable? audioScope) : IDisposable
    {
        private IDisposable? m_sessionScope = sessionScope;
        private IDisposable? m_audioScope = audioScope;

        /// <summary>
        /// Releases the presentation and audio execution scopes in reverse acquisition order.
        /// </summary>
        public void Dispose()
        {
            IDisposable? session = m_sessionScope;
            IDisposable? audio = m_audioScope;
            m_sessionScope = null;
            m_audioScope = null;
            try
            {
                session?.Dispose();
            }
            finally
            {
                audio?.Dispose();
            }
        }
    }

    private sealed class PlayModeReloadTransaction(EditorPlayModeController owner)
        : IGenerationChange
    {
        private bool m_prepared;

        /// <summary>
        /// Releases the isolated Play world before the candidate generation becomes active.
        /// </summary>
        public void PrepareForActivation()
        {
            if (m_prepared)
                return;
            owner.Quiesce();
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
