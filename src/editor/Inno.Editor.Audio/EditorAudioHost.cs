using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets;
using Inno.Audio;
using Inno.Audio.MiniAudio;
using Inno.Audio.Runtime;
using Inno.Audio.Scene;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Runtime;

namespace Inno.Editor.Audio;

/// <summary>
/// Owns Editor audio devices, previews, diagnostics, and isolated Play Mode audio generations.
/// </summary>
public sealed class EditorAudioHost : IEditorAudioHost
{
    private readonly IAssetArtifactLookup m_artifacts;
    private readonly Func<IAudioDevice>? m_deviceFactory;
    private readonly EditorAudioDiagnosticSink m_diagnostics;
    private readonly Func<AudioProjectSettings> m_settingsProvider;
    private readonly Dictionary<RuntimeSession, AudioRuntimeLayer> m_sessions = [];
    private readonly TypeCatalog m_types;
    private bool m_disposed;

    /// <summary>
    /// Creates the Editor audio host over the active authoring artifact lookup and settings source.
    /// </summary>
    /// <param name="types">
    /// Active type catalog used for provider and mixer extension generations.
    /// </param>
    /// <param name="artifacts">
    /// Authoring artifact lookup shared by Edit and isolated Play sessions.
    /// </param>
    /// <param name="logs">
    /// Editor log router that receives deduplicated audio diagnostics.
    /// </param>
    /// <param name="settingsProvider">
    /// Host-owned callback that returns the latest isolated audio project settings snapshot.
    /// </param>
    /// <param name="deviceFactory">
    /// Optional host-owned backend factory, primarily for platform composition and headless validation.
    /// </param>
    public EditorAudioHost(
        TypeCatalog types,
        IAssetArtifactLookup artifacts,
        LogRouter logs,
        Func<AudioProjectSettings>? settingsProvider = null,
        Func<IAudioDevice>? deviceFactory = null)
    {
        m_types = types ?? throw new ArgumentNullException(nameof(types));
        m_artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        m_diagnostics = new EditorAudioDiagnosticSink(logs ?? throw new ArgumentNullException(nameof(logs)));
        m_settingsProvider = settingsProvider ?? (() => new AudioProjectSettings());
        m_deviceFactory = deviceFactory;
    }

    /// <summary>
    /// Creates and owns one audio runtime for an isolated Editor runtime session.
    /// </summary>
    /// <param name="session">
    /// Edit or Play Mode session to bind.
    /// </param>
    /// <returns>
    /// A lease that releases the audio runtime before the owning session is disposed.
    /// </returns>
    public IDisposable BeginSession(RuntimeSession session)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(session);
        if (m_sessions.ContainsKey(session))
            throw new InvalidOperationException("The runtime session already owns an Editor audio generation.");
        if (session.options.kind == RuntimeSessionKind.Play &&
            m_sessions.Keys.Any(static active => active.options.kind == RuntimeSessionKind.Play))
        {
            throw new InvalidOperationException("Only one isolated Play Mode audio generation may be active.");
        }

        AudioProjectSettings settings = m_settingsProvider()
            ?? throw new InvalidOperationException("The audio settings provider returned null.");
        IAudioDevice device = CreateDevice();
        var runtime = new AudioRuntimeLayer(
            m_types,
            device,
            m_artifacts,
            session.events,
            m_diagnostics,
            new AudioRuntimeOptions
            {
                maxVoices = settings.maxVoices,
                decodedCacheBudgetBytes = settings.decodedCacheBudgetBytes,
                automaticStreamingThresholdBytes = settings.automaticStreamingThresholdBytes
            },
            new SceneAudioContent(session.scenes).Capture,
            m_deviceFactory is null ? static () => new MiniAudioDevice() : null);
        try
        {
            if (settings.defaultMixer is not null && !runtime.ApplyMixer(settings.defaultMixer))
                throw new InvalidOperationException("The configured default Editor audio mixer could not be activated.");
            if (!runtime.SetBusVolume(AudioBusId.master, settings.masterVolume))
                throw new InvalidOperationException("The configured Editor master audio volume could not be applied.");
            m_sessions.Add(session, runtime);
            if (session.options.kind == RuntimeSessionKind.Play)
                SetEditModePaused(true);
            return new SessionLease(this, session);
        }
        catch
        {
            runtime.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Binds the script-facing audio façade to one session's runtime.
    /// </summary>
    /// <param name="session">
    /// Session with an active audio lease.
    /// </param>
    /// <returns>
    /// A strict last-in-first-out execution scope.
    /// </returns>
    public IDisposable EnterExecutionScope(RuntimeSession session)
        => GetRuntime(session).EnterExecutionScope();

    /// <summary>
    /// Advances one session's providers, native graph, and completion dispatch at a frame-safe point.
    /// </summary>
    /// <param name="session">
    /// Session with an active audio lease.
    /// </param>
    /// <param name="deltaTime">
    /// Non-negative elapsed frame time in seconds.
    /// </param>
    public void Update(RuntimeSession session, float deltaTime)
        => GetRuntime(session).Update(deltaTime);

    /// <summary>
    /// Starts an Editor-owned preview voice through an active Edit Mode session.
    /// </summary>
    /// <param name="session">
    /// Active Edit Mode session.
    /// </param>
    /// <param name="clip">
    /// Imported audio clip to preview.
    /// </param>
    /// <param name="options">
    /// Optional playback parameters; omitted values use engine defaults.
    /// </param>
    /// <returns>
    /// The preview voice handle, initially in the preparing state.
    /// </returns>
    public AudioVoiceHandle PlayPreview(
        RuntimeSession session,
        AudioClipAsset clip,
        AudioPlayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clip);
        if (session.options.kind != RuntimeSessionKind.Edit)
            throw new ArgumentException("Audio previews require an Edit Mode runtime session.", nameof(session));
        return GetRuntime(session).Play(clip, options ?? AudioPlayOptions.defaultValue);
    }

    /// <summary>
    /// Stops one preview voice owned by an active Edit Mode session.
    /// </summary>
    /// <param name="session">
    /// Active Edit Mode session.
    /// </param>
    /// <param name="voice">
    /// Preview voice to stop.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a live preview was stopped.
    /// </returns>
    public bool StopPreview(RuntimeSession session, AudioVoiceHandle voice)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.options.kind != RuntimeSessionKind.Edit)
            return false;
        return GetRuntime(session).Stop(voice);
    }

    /// <summary>
    /// Releases every active audio generation before their owning Editor sessions are torn down.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        List<Exception>? failures = null;
        foreach (AudioRuntimeLayer runtime in m_sessions.Values.Reverse().ToArray())
        {
            try
            {
                runtime.Dispose();
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }
        m_sessions.Clear();
        if (failures is not null)
            throw new AggregateException("One or more Editor audio generations could not be released.", failures);
    }

    private IAudioDevice CreateDevice()
    {
        try
        {
            return m_deviceFactory?.Invoke() ?? new MiniAudioDevice();
        }
        catch (Exception exception)
        {
            m_diagnostics.Publish(new AudioDiagnostic(
                "AUDIO_DEVICE_INIT_FAILED",
                $"The Editor audio output could not start; this audio generation is explicitly muted: {exception.Message}",
                AudioDiagnosticSeverity.Warning,
                "MiniAudio"));
            return new MutedAudioDevice();
        }
    }

    private AudioRuntimeLayer GetRuntime(RuntimeSession session)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(session);
        return m_sessions.TryGetValue(session, out AudioRuntimeLayer? runtime)
            ? runtime
            : throw new InvalidOperationException("The runtime session has no active Editor audio lease.");
    }

    private void EndSession(RuntimeSession session)
    {
        if (!m_sessions.Remove(session, out AudioRuntimeLayer? runtime))
            return;
        bool wasPlayMode = session.options.kind == RuntimeSessionKind.Play;
        try
        {
            runtime.Dispose();
        }
        finally
        {
            if (wasPlayMode)
                SetEditModePaused(false);
        }
    }

    private void SetEditModePaused(bool paused)
    {
        foreach ((RuntimeSession session, AudioRuntimeLayer runtime) in m_sessions)
        {
            if (session.options.kind == RuntimeSessionKind.Edit)
                _ = runtime.SetBusPaused(AudioBusId.master, paused);
        }
    }

    private sealed class SessionLease(EditorAudioHost owner, RuntimeSession session) : IDisposable
    {
        private EditorAudioHost? m_owner = owner;

        /// <summary>
        /// Ends the owned session exactly once.
        /// </summary>
        public void Dispose()
        {
            EditorAudioHost? current = m_owner;
            m_owner = null;
            current?.EndSession(session);
        }
    }

    private sealed class EditorAudioDiagnosticSink : IAudioDiagnosticSink
    {
        private readonly HashSet<AudioDiagnosticIdentity> m_active = [];
        private readonly Logger m_log;

        internal EditorAudioDiagnosticSink(LogRouter logs)
        {
            m_log = logs.CreateLogger<EditorAudioHost>();
        }

        /// <summary>
        /// Writes a new structured audio diagnostic to the Editor log once per identity.
        /// </summary>
        /// <param name="diagnostic">
        /// Diagnostic emitted by the active audio runtime.
        /// </param>
        public void Publish(AudioDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            var identity = new AudioDiagnosticIdentity(
                diagnostic.code,
                diagnostic.source,
                diagnostic.message,
                diagnostic.severity);
            if (!m_active.Add(identity))
                return;
            LogLevel level = diagnostic.severity switch
            {
                AudioDiagnosticSeverity.Error => LogLevel.Error,
                AudioDiagnosticSeverity.Warning => LogLevel.Warn,
                _ => LogLevel.Info
            };
            m_log.Write(level, "[{0}] {1}", [diagnostic.code, diagnostic.message]);
        }

        private readonly record struct AudioDiagnosticIdentity(
            string code,
            string? source,
            string message,
            AudioDiagnosticSeverity severity);
    }
}
