using Inno.Runtime.Contracts;
using Inno.Core.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets;
using Inno.Audio;
using Inno.Audio.Runtime;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Runtime;
using Inno.Scene;

namespace Inno.Editor.Audio;

/// <summary>
/// Owns Editor audio devices, previews, diagnostics, and isolated Play Mode audio generations.
/// </summary>
public sealed class EditorAudioHost : IEditorAudioHost
{
    private readonly IAssetArtifactLookup m_artifacts;
    private readonly Func<IAudioDevice> m_deviceFactory;
    private readonly DiagnosticHub m_diagnostics;
    private readonly Func<AudioProjectSettings> m_settingsProvider;
    private readonly Dictionary<RuntimeSession, AudioRuntime> m_sessions = [];
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
    /// <param name="diagnostics">
    /// Shared Editor diagnostic hub that owns current audio issues.
    /// </param>
    /// <param name="deviceFactory">
    /// Required host-owned backend-neutral device factory.
    /// </param>
    /// <param name="settingsProvider">
    /// Optional host-owned callback that returns the latest isolated audio project settings snapshot.
    /// </param>
    public EditorAudioHost(
        TypeCatalog types,
        IAssetArtifactLookup artifacts,
        DiagnosticHub diagnostics,
        Func<IAudioDevice> deviceFactory,
        Func<AudioProjectSettings>? settingsProvider = null)
    {
        m_types = types ?? throw new ArgumentNullException(nameof(types));
        m_artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        m_diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        m_settingsProvider = settingsProvider ?? (() => new AudioProjectSettings());
        m_deviceFactory = deviceFactory ?? throw new ArgumentNullException(nameof(deviceFactory));
    }

    /// <summary>
    /// Creates the reusable feature factory used by Edit and Play runtime sessions.
    /// </summary>
    /// <param name="session">
    /// The explicit session owner; it is never exposed through subsystem contracts.
    /// </param>
    /// <returns>
    /// A factory that creates and releases one isolated audio generation per session.
    /// </returns>
    public IRuntimeSubsystemFactory CreateRuntimeSubsystemFactory(RuntimeSession session)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        return new EditorAudioRuntimeFactory(this, session);
    }

    private AudioRuntime CreateRuntime(RuntimeSession session, RuntimeSubsystemContext context)
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
        DiagnosticReporter diagnostics = context.resources.Own(m_diagnostics.CreateReporter(
            new DiagnosticSource($"inno.editor.audio.{session.sessionId}", $"Audio ({session.options.kind})")));
        IAudioDevice device = CreateDevice(diagnostics);
        AudioRuntime? runtime = null;
        try
        {
            runtime = new AudioRuntime(
            m_types,
            device,
            m_artifacts,
            session.events,
            diagnostics,
            new AudioRuntimeOptions
            {
                maxVoices = settings.maxVoices,
                decodedCacheBudgetBytes = settings.decodedCacheBudgetBytes,
                automaticStreamingThresholdBytes = settings.automaticStreamingThresholdBytes
            },
            contentScopeProvider: () => SceneContentSource.CreateScope(session.scenes),
            deviceRecoveryFactory: m_deviceFactory);
            if (settings.defaultMixer is not null && !runtime.ApplyMixer(settings.defaultMixer))
                throw new InvalidOperationException("The configured default Editor audio mixer could not be activated.");
            if (!runtime.SetBusVolume(AudioBusId.master, settings.masterVolume))
                throw new InvalidOperationException("The configured Editor master audio volume could not be applied.");
            m_sessions.Add(session, runtime);
            if (session.options.kind == RuntimeSessionKind.Play)
                SetEditModePaused(true);
            return runtime;
        }
        catch (Exception failure)
        {
            try
            {
                if (runtime is not null)
                    runtime.Dispose();
                else
                    device.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Editor audio startup and rollback failed.", failure, cleanup);
            }
            throw;
        }
    }

    /// <summary>
    /// Binds the script-facing audio façade to one session's runtime.
    /// </summary>
    /// <param name="session">
    /// Session whose feature pipeline owns an active Editor audio generation.
    /// </param>
    /// <returns>
    /// A strict last-in-first-out execution scope.
    /// </returns>
    public IDisposable EnterExecutionScope(RuntimeSession session)
        => GetRuntime(session).EnterExecutionScope();

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
        foreach (AudioRuntime runtime in m_sessions.Values.Reverse().ToArray())
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

    private IAudioDevice CreateDevice(IDiagnosticReporter diagnostics)
    {
        try
        {
            return m_deviceFactory();
        }
        catch (Exception exception)
        {
            diagnostics.Publish(new Diagnostic(
                "AUDIO_DEVICE_INIT_FAILED",
                $"The Editor audio output could not start; this audio generation is explicitly muted: {exception.Message}",
                DiagnosticSeverity.Warning,
                "AudioAdapter"));
            return new MutedAudioDevice();
        }
    }

    private AudioRuntime GetRuntime(RuntimeSession session)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(session);
        return m_sessions.TryGetValue(session, out AudioRuntime? runtime)
            ? runtime
            : throw new InvalidOperationException("The runtime session has no active Editor audio lease.");
    }

    private void ReleaseRuntime(RuntimeSession session, AudioRuntime runtime)
    {
        if (!m_sessions.TryGetValue(session, out AudioRuntime? active) ||
            !ReferenceEquals(active, runtime))
        {
            return;
        }
        m_sessions.Remove(session);
        bool wasPlayMode = session.options.kind == RuntimeSessionKind.Play;
        if (wasPlayMode)
            SetEditModePaused(false);
    }

    private void SetEditModePaused(bool paused)
    {
        foreach ((RuntimeSession session, AudioRuntime runtime) in m_sessions)
        {
            if (session.options.kind == RuntimeSessionKind.Edit)
                _ = runtime.SetBusPaused(AudioBusId.master, paused);
        }
    }

    private sealed class EditorAudioRuntimeFactory(EditorAudioHost owner, RuntimeSession session)
        : IRuntimeSubsystemFactory
    {
        /// <summary>
        /// Gets the stable audio feature descriptor shared by every Editor session.
        /// </summary>
        public RuntimeSubsystemDescriptor descriptor { get; } = new(
            new RuntimeSubsystemId("inno.runtime.audio"),
            order: 500,
            dependencies: [new RuntimeSubsystemId("inno.runtime.scene")]);

        /// <summary>
        /// Creates one Editor-owned audio generation for the supplied runtime session.
        /// </summary>
        /// <param name="context">
        /// The isolated session that owns the resulting feature.
        /// </param>
        /// <returns>
        /// A feature that releases its audio generation through the owning Editor host.
        /// </returns>
        public IRuntimeSubsystem Create(RuntimeSubsystemContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            AudioRuntime runtime = owner.CreateRuntime(session, context);
            context.resources.Own(new SessionAudioRegistration(owner, session, runtime));
            return runtime;
        }
    }




    private sealed class SessionAudioRegistration(EditorAudioHost owner, RuntimeSession session, AudioRuntime runtime) : IDisposable
    {
        /// <summary>
        /// Removes only this session's runtime registration from the editor audio owner.
        /// </summary>
        public void Dispose() => owner.ReleaseRuntime(session, runtime);
    }
}
