using Inno.References;
using Inno.Runtime.Contracts;
using Inno.Core.Diagnostics;
using Inno.Core.Execution;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Core.Events;
using Inno.Extensibility.Types;

namespace Inno.Audio.Runtime;

/// <summary>
/// Owns playback scheduling, clip retention, mixer generations, and content synchronization.
/// </summary>
public sealed class AudioRuntime : RuntimeSubsystem, IAudioService
{
    private readonly IAssetArtifactLookup m_artifacts;
    private readonly Func<ContentReadScope>? m_contentScopeProvider;
    private readonly Func<IAudioDevice>? m_deviceRecoveryFactory;
    private readonly IDiagnosticReporter m_diagnostics;
    private readonly EventDispatcher m_events;
    private readonly AudioExtensionRegistry m_extensions;
    private AudioClipCache m_clipCache;
    private AudioVoiceOwner m_voices;
    private AudioContentOwner m_content;
    private readonly AudioRuntimeOptions m_options;

    private IAudioDevice m_device;
    private AudioMixerOwner m_mixer;
    private string? m_mixerDiagnosticSource;
    private long m_stolenVoiceCount;
    private float m_deviceRecoveryElapsed;
    private bool m_deviceLossReported;
    private bool m_disposed;
    private bool m_stopping;
    private bool m_extensionsRetired;
    private int m_retirementStage;
    private readonly List<Exception> m_retirementFailures = [];
    private AudioDeviceCandidate? m_candidateRetirement;

    /// <summary>
    /// Creates an audio runtime over one backend device generation.
    /// </summary>
    /// <param name="types">
    /// Type catalog used for mixer and content-provider discovery.
    /// </param>
    /// <param name="device">
    /// Backend device whose ownership transfers to this runtime.
    /// </param>
    /// <param name="artifacts">
    /// Lookup for verified immutable <c>audio-data</c> artifacts.
    /// </param>
    /// <param name="events">
    /// Main-thread event dispatcher that receives voice completion events.
    /// </param>
    /// <param name="diagnostics">
    /// Required owner-bound diagnostic producer.
    /// </param>
    /// <param name="options">
    /// Optional bounded runtime resource policies.
    /// </param>
    /// <param name="contentScopeProvider">
    /// Optional callback that supplies update-scoped host content without coupling Runtime to Scene.
    /// </param>
    /// <param name="deviceRecoveryFactory">
    /// Optional host factory used to retry a real output device at main-thread update safety points.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// A required device, catalog, artifact lookup, event dispatcher, or diagnostic reporter is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A voice, preload, or content limit is not positive, a byte budget is negative, or the recovery delay is invalid.
    /// </exception>
    public AudioRuntime(
        TypeCatalog types,
        IAudioDevice device,
        IAssetArtifactLookup artifacts,
        EventDispatcher events,
        IDiagnosticReporter diagnostics,
        AudioRuntimeOptions? options = null,
        Func<ContentReadScope>? contentScopeProvider = null,
        Func<IAudioDevice>? deviceRecoveryFactory = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        m_device = device ?? throw new ArgumentNullException(nameof(device));
        m_artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        m_events = events ?? throw new ArgumentNullException(nameof(events));
        m_diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        m_options = (options ?? new AudioRuntimeOptions()).Validate();
        m_contentScopeProvider = contentScopeProvider;
        m_deviceRecoveryFactory = deviceRecoveryFactory;
        m_extensions = new AudioExtensionRegistry(types);
        m_clipCache = new AudioClipCache(m_device, m_artifacts, m_options, m_extensions.RetireBackend);
        try { m_mixer = new AudioMixerOwner(m_device, m_extensions.Fault, m_extensions.RetireBackend); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch
        {
            m_extensions.Dispose();
            throw;
        }
        m_voices = new AudioVoiceOwner(m_device, m_artifacts, m_clipCache, m_mixer, m_events, m_diagnostics, m_options, m_extensions.RetireBackend);
        m_content = new AudioContentOwner(m_device, m_voices, m_diagnostics, m_contentScopeProvider, m_options.maxContentSnapshots);
        if (m_device.state == AudioDeviceState.Muted)
        {
            Publish(
                "AUDIO_NO_DEVICE",
                "Audio is running in explicit muted mode because no output device is active.",
                DiagnosticSeverity.Warning,
                m_device.GetType().FullName);
        }
    }

    /// <summary>
    /// Gets immutable capabilities for the current backend generation.
    /// </summary>
    public AudioCapabilities capabilities => m_device.capabilities;

    /// <summary>
    /// Gets the current output availability state.
    /// </summary>
    public AudioDeviceState deviceState => m_device.state;

    /// <summary>
    /// Gets the monotonic backend audio clock in seconds.
    /// </summary>
    public double dspTime => m_device.dspTime;
    /// <summary>
    /// Captures snapshots and binds service façades.
    /// </summary>
    /// <param name="frame">
    /// The variable frame timing.
    /// </param>
    protected override void OnBeginFrame(RuntimeFrame frame) => OwnFrameScope(EnterExecutionScope());
    /// <summary>
    /// Advances domain state on the variable clock.
    /// </summary>
    /// <param name="frame">
    /// The variable frame timing.
    /// </param>
    protected override void OnUpdate(RuntimeFrame frame) => Update(frame.unscaledDeltaTime);

    /// <summary>
    /// Gets current runtime resource statistics.
    /// </summary>
    public AudioStatistics statistics
        => new(
            m_voices.count,
            m_clipCache.count,
            m_clipCache.decodedBytes,
            m_stolenVoiceCount + m_voices.stolenCount);

    /// <summary>
    /// Gets bounded provider admission counts from the most recent content collection on this device generation.
    /// </summary>
    public AudioContentStatistics contentStatistics => m_content.statistics;

    /// <summary>
    /// Binds this runtime to script-facing audio APIs for the current asynchronous flow.
    /// </summary>
    /// <returns>
    /// A strict last-in-first-out execution scope owned by the caller.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this runtime has been disposed.
    /// </exception>
    public IDisposable EnterExecutionScope()
    {
        EnsureActive();
        return AudioExecutionContext.EnterScope(this);
    }

    /// <summary>
    /// Starts one clip using default playback parameters.
    /// </summary>
    /// <param name="clip">
    /// Imported clip to prepare and play.
    /// </param>
    /// <returns>
    /// A preparing voice handle owned by the current device generation.
    /// </returns>
    public AudioVoiceHandle Play(AudioClipAsset clip) => Play(clip, AudioPlayOptions.defaultValue);

    /// <summary>
    /// Starts one clip using explicit playback parameters.
    /// </summary>
    /// <param name="clip">
    /// Imported clip to prepare and play.
    /// </param>
    /// <param name="options">
    /// Immutable playback parameters.
    /// </param>
    /// <returns>
    /// A preparing voice handle owned by the current device generation.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Playback options are uninitialized. The request is rejected before asset acquisition or voice stealing.
    /// </exception>
    public AudioVoiceHandle Play(AudioClipAsset clip, AudioPlayOptions options)
    {
        EnsureActive();
        return m_voices.Play(clip, options, null);
    }

    /// <summary>
    /// Schedules one clip against the monotonic audio clock.
    /// </summary>
    /// <param name="clip">
    /// Imported clip to prepare and play.
    /// </param>
    /// <param name="scheduledDspTime">
    /// Absolute audio-clock start time in seconds.
    /// </param>
    /// <param name="options">
    /// Immutable playback parameters.
    /// </param>
    /// <returns>
    /// A preparing voice handle owned by the current device generation.
    /// </returns>
    public AudioVoiceHandle PlayScheduled(
        AudioClipAsset clip,
        double scheduledDspTime,
        AudioPlayOptions options)
    {
        if (double.IsNaN(scheduledDspTime) || double.IsInfinity(scheduledDspTime) || scheduledDspTime < 0d)
            throw new ArgumentOutOfRangeException(nameof(scheduledDspTime));
        EnsureActive();
        return m_voices.Play(clip, options, scheduledDspTime);
    }

    /// <summary>
    /// Stops a live or preparing voice.
    /// </summary>
    /// <param name="voice">
    /// Runtime voice handle to stop.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a live voice was stopped.
    /// </returns>
    public bool Stop(AudioVoiceHandle voice)
    {
        EnsureActive();
        return m_voices.Stop(voice);
    }

    /// <summary>
    /// Pauses a live or preparing voice.
    /// </summary>
    /// <param name="voice">
    /// Runtime voice handle to pause.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the voice entered the paused state.
    /// </returns>
    public bool Pause(AudioVoiceHandle voice)
    {
        EnsureActive();
        return m_voices.Pause(voice);
    }

    /// <summary>
    /// Resumes a paused voice.
    /// </summary>
    /// <param name="voice">
    /// Runtime voice handle to resume.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the voice resumed or returned to preparation.
    /// </returns>
    public bool Resume(AudioVoiceHandle voice)
    {
        EnsureActive();
        return m_voices.Resume(voice);
    }

    /// <summary>
    /// Moves a live or preparing voice cursor to a clip-relative position.
    /// </summary>
    /// <param name="voice">
    /// Runtime voice handle to seek.
    /// </param>
    /// <param name="position">
    /// Non-negative clip-relative position.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the cursor request was accepted.
    /// </returns>
    public bool Seek(AudioVoiceHandle voice, TimeSpan position)
    {
        EnsureActive();
        return m_voices.Seek(voice, position);
    }

    /// <summary>
    /// Replaces mutable parameters for a live or preparing voice.
    /// </summary>
    /// <param name="voice">
    /// Runtime voice handle to update.
    /// </param>
    /// <param name="parameters">
    /// Current voice parameter snapshot.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the parameter request was accepted.
    /// </returns>
    public bool SetVoiceParameters(AudioVoiceHandle voice, AudioVoiceParameters parameters)
    {
        EnsureActive();
        return m_voices.SetVoiceParameters(voice, parameters);
    }

    /// <summary>
    /// Queries the current state of a runtime voice.
    /// </summary>
    /// <param name="voice">
    /// Runtime voice handle to query.
    /// </param>
    /// <param name="playbackState">
    /// Receives the current state when the handle is known.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for active and terminal handles from this runtime generation.
    /// </returns>
    public bool TryGetVoiceState(AudioVoiceHandle voice, out AudioPlaybackState playbackState)
    {
        EnsureActive();
        return m_voices.TryGetVoiceState(voice, out playbackState);
    }

    /// <summary>
    /// Updates linear gain for a semantic mixer bus.
    /// </summary>
    /// <param name="bus">
    /// Stable bus identifier.
    /// </param>
    /// <param name="volume">
    /// Non-negative linear gain.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the active mixer contains the bus.
    /// </returns>
    public bool SetBusVolume(AudioBusId bus, float volume)
    {
        EnsureActive();
        return m_mixer.SetBusVolume(bus, volume);
    }

    /// <summary>
    /// Updates mute state for a semantic mixer bus.
    /// </summary>
    /// <param name="bus">
    /// Stable bus identifier.
    /// </param>
    /// <param name="muted">
    /// Whether output from the bus is silenced.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the active mixer contains the bus.
    /// </returns>
    public bool SetBusMuted(AudioBusId bus, bool muted)
    {
        EnsureActive();
        return m_mixer.SetBusMuted(bus, muted);
    }

    /// <summary>
    /// Updates pause state for a semantic mixer bus.
    /// </summary>
    /// <param name="bus">
    /// Stable bus identifier.
    /// </param>
    /// <param name="paused">
    /// Whether routed voices should pause.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the active mixer contains the bus.
    /// </returns>
    public bool SetBusPaused(AudioBusId bus, bool paused)
    {
        EnsureActive();
        return m_mixer.SetBusPaused(bus, paused);
    }

    /// <summary>
    /// Prepares and explicitly retains a clip cache entry.
    /// </summary>
    /// <param name="clip">
    /// Imported clip to retain.
    /// </param>
    /// <param name="loadMode">
    /// Storage strategy override.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token observed before the cache commit.
    /// </param>
    /// <returns>
    /// An operation completed at an update safety point after native preparation succeeds or fails.
    /// </returns>
    public ValueTask PreloadAsync(
        AudioClipAsset clip,
        AudioClipLoadMode loadMode = AudioClipLoadMode.Automatic,
        CancellationToken cancellationToken = default)
    {
        EnsureActive();
        return m_clipCache.PreloadAsync(clip, loadMode, cancellationToken);
    }

    /// <summary>
    /// Releases one explicit preload retention without interrupting voices.
    /// </summary>
    /// <param name="clip">
    /// Imported clip whose preload retention should be released.
    /// </param>
    public void ReleasePreload(AudioClipAsset clip)
    {
        EnsureActive();
        m_clipCache.ReleasePreload(clip);
    }

    /// <summary>
    /// Builds and atomically installs a mixer graph from reloadable extensions.
    /// </summary>
    /// <param name="asset">
    /// Mixer asset containing stable extension identifiers and neutral state.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every extension resolved and the candidate became active.
    /// </returns>
    public bool ApplyMixer(AudioMixerAsset asset)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(asset);
        if (m_mixerDiagnosticSource is not null)
        {
            m_diagnostics.Resolve("AUDIO_MIXER_EXTENSION_MISSING", m_mixerDiagnosticSource);
            m_diagnostics.Resolve("AUDIO_MIXER_CANDIDATE_FAILED", m_mixerDiagnosticSource);
        }
        m_mixerDiagnosticSource = asset.mixerTypeId;
        try
        {
            if (!m_extensions.extensions.TryBuildMixer(asset, out AudioMixer? mixer) || mixer is null)
            {
                Publish(
                    "AUDIO_MIXER_EXTENSION_MISSING",
                    "The candidate audio mixer references an unavailable extension; the last-good mixer remains active.",
                    DiagnosticSeverity.Error,
                    asset.mixerTypeId);
                return false;
            }
            m_mixer.Install(mixer, m_voices.usedBuses);
            return true;
        }
        catch (Exception exception)
        {
            m_extensions.EnsureHealthy();
            Publish(
                "AUDIO_MIXER_CANDIDATE_FAILED",
                $"The candidate audio mixer failed validation; the last-good mixer remains active: {exception.Message}",
                DiagnosticSeverity.Error,
                asset.mixerTypeId);
            return false;
        }
    }

    /// <summary>
    /// Replaces a lost or muted backend at a main-thread safety point.
    /// </summary>
    /// <param name="replacement">
    /// Initialized replacement device, consumed on success and disposed if preparation fails.
    /// </param>
    /// <exception cref="AggregateException">
    /// Retirement failed after publication preparation; the generation gate is Faulted and the host must restart.
    /// </exception>
    /// <exception cref="RetirementTimeoutException">
    /// Old or candidate work did not drain before its deadline. Both owners remain retained and the host must restart.
    /// </exception>
    public void ReplaceDevice(IAudioDevice replacement)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(replacement);
        if (ReferenceEquals(replacement, m_device))
            throw new ArgumentException("The replacement must be a different device instance.", nameof(replacement));
        AudioMixerOwner replacementMixer;
        m_candidateRetirement = new AudioDeviceCandidate(replacement);
        try { replacementMixer = m_candidateRetirement.Prepare(m_mixer); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception failure)
        {
            try { m_extensions.RetireBackend(m_candidateRetirement.Dispose); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception cleanup)
            {
                m_extensions.Fault(cleanup);
                throw new AggregateException("Audio device preparation and candidate retirement failed.", failure, cleanup);
            }
            m_candidateRetirement = null;
            throw;
        }
        List<Exception> failures = [];
        m_extensions.RetireBackend(() => failures = RetireBackend(AudioCompletionReason.DeviceLost));
        if (failures.Count > 0)
        {
            try { m_extensions.RetireBackend(m_candidateRetirement.Dispose); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception error) { failures.Add(error); }
            m_candidateRetirement = null;
            var failure = new AggregateException("Audio device retirement failed; the previous generation cannot resume.", failures);
            m_extensions.Fault(failure);
            throw failure;
        }
        string? previousSource = m_device.GetType().FullName;
        m_candidateRetirement.Commit();
        m_candidateRetirement = null;
        m_retirementStage = 0;
        m_stolenVoiceCount += m_voices.stolenCount;
        m_device = replacement;
        m_clipCache = new AudioClipCache(m_device, m_artifacts, m_options, m_extensions.RetireBackend);
        m_mixer = replacementMixer;
        m_voices = new AudioVoiceOwner(m_device, m_artifacts, m_clipCache, m_mixer, m_events, m_diagnostics, m_options, m_extensions.RetireBackend, m_voices);
        m_content = new AudioContentOwner(m_device, m_voices, m_diagnostics, m_contentScopeProvider, m_options.maxContentSnapshots);
        m_deviceRecoveryElapsed = 0f;
        m_deviceLossReported = false;
        m_diagnostics.Resolve("AUDIO_NO_DEVICE", previousSource);
        m_diagnostics.Resolve("AUDIO_DEVICE_LOST", previousSource);
        m_diagnostics.Resolve("AUDIO_DEVICE_RECOVERY_FAILED", "AudioRuntime");
    }

    /// <summary>
    /// Attempts to replace a muted or lost output generation while preserving the active mixer graph.
    /// </summary>
    /// <param name="deviceFactory">
    /// Host-owned factory that creates a fresh initialized device candidate.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when output is already ready or a ready candidate was installed;
    /// otherwise <see langword="false"/> and the current muted or lost generation remains active.
    /// </returns>
    public bool TryRecoverDevice(Func<IAudioDevice> deviceFactory)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(deviceFactory);
        if (m_device.state == AudioDeviceState.Ready)
            return true;

        IAudioDevice? candidate = null;
        try
        {
            candidate = deviceFactory()
                ?? throw new InvalidOperationException("The audio recovery factory returned null.");
            if (candidate.state != AudioDeviceState.Ready)
            {
                IAudioDevice rejected = candidate;
                candidate = null;
                try { m_extensions.RetireBackend(rejected.Dispose); }
                catch (Exception cleanup) { m_extensions.Fault(cleanup); throw; }
                Publish(
                    "AUDIO_DEVICE_RECOVERY_FAILED",
                    "The replacement audio device did not reach the ready state; muted playback remains active.",
                    DiagnosticSeverity.Warning,
                    "AudioRuntime");
                return false;
            }
            string? recoveredSource = candidate.GetType().FullName;
            IAudioDevice prepared = candidate;
            candidate = null;
            ReplaceDevice(prepared);
            Publish(
                "AUDIO_DEVICE_RECOVERED",
                "The audio output device recovered with a new generation.",
                DiagnosticSeverity.Info,
                recoveredSource);
            return true;
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception)
        {
            try { if (candidate is not null) m_extensions.RetireBackend(candidate.Dispose); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception cleanup)
            {
                m_extensions.Fault(cleanup);
                throw new AggregateException("Audio recovery and candidate retirement failed.", exception, cleanup);
            }
            m_extensions.EnsureHealthy();
            Publish(
                "AUDIO_DEVICE_RECOVERY_FAILED",
                $"The audio output recovery candidate failed; muted playback remains active: {exception.Message}",
                DiagnosticSeverity.Warning,
                "AudioRuntime");
            return false;
        }
    }

    /// <summary>
    /// Advances providers, pending preparation, backend maintenance, and completion dispatch at a main-thread safety point.
    /// </summary>
    /// <param name="deltaTime">
    /// Non-negative elapsed frame time in seconds.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The elapsed time is negative or non-finite.
    /// </exception>
    public void Update(float deltaTime)
    {
        EnsureActive();
        if (!float.IsFinite(deltaTime) || deltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        m_content.Update(m_extensions.extensions.providers, deltaTime);
        m_voices.PrepareVoices();
        m_device.Update(deltaTime);
        m_clipCache.CompletePreloads();
        m_voices.DrainCompletions();
        m_voices.RefreshStates();
        m_mixer.CollectRetiredBuses(m_voices.usedBuses);
        if (m_device.state == AudioDeviceState.Lost)
        {
            if (!m_deviceLossReported)
            {
                m_deviceLossReported = true;
                m_diagnostics.Resolve("AUDIO_DEVICE_RECOVERED", m_device.GetType().FullName);
                Publish(
                    "AUDIO_DEVICE_LOST",
                    "The audio output device was lost; active voices are completing and recovery will retry at safe points.",
                    DiagnosticSeverity.Warning,
                    m_device.GetType().FullName);
            }
            m_voices.CompleteAll(AudioCompletionReason.DeviceLost);
            m_content.ClearEmitters();
        }
        AdvanceDeviceRecovery(deltaTime);
    }

    /// <summary>
    /// Stops voices and releases providers, cache entries, mixer generations, and the owned backend device.
    /// </summary>
    protected override void OnStop()
    {
        if (m_disposed)
            return;
        m_stopping = true;
        if (!m_extensionsRetired)
        {
            try { m_extensions.Dispose(); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception) { m_retirementFailures.Add(exception); }
            m_extensionsRetired = true;
        }
        List<Exception> failures = RetireBackend(AudioCompletionReason.Stopped);
        try { m_candidateRetirement?.Dispose(); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception) { failures.Add(exception); }
        m_candidateRetirement = null;
        m_disposed = true;
        if (failures.Count > 0)
            throw new AggregateException("Audio retirement failed after all resource owners were attempted.", failures);
    }

    private List<Exception> RetireBackend(AudioCompletionReason reason)
    {
        while (m_retirementStage < 5)
        {
            try
            {
                switch (m_retirementStage)
                {
                    case 0: m_voices.CompleteAll(reason); break;
                    case 1: m_content.Dispose(); break;
                    case 2: m_clipCache.Dispose(); break;
                    case 3: m_mixer.Dispose(); break;
                    case 4: m_device.Dispose(); break;
                }
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception) { m_retirementFailures.Add(exception); }
            m_retirementStage++;
        }
        List<Exception> failures = [.. m_retirementFailures];
        m_retirementFailures.Clear();
        return failures;
    }

    private void AdvanceDeviceRecovery(float deltaTime)
    {
        if (m_device.state == AudioDeviceState.Ready)
        {
            m_deviceRecoveryElapsed = 0f;
            m_deviceLossReported = false;
            return;
        }
        if (m_deviceRecoveryFactory is null)
            return;
        m_deviceRecoveryElapsed += deltaTime;
        if (m_deviceRecoveryElapsed < m_options.deviceRecoveryIntervalSeconds)
            return;
        m_deviceRecoveryElapsed = 0f;
        _ = TryRecoverDevice(m_deviceRecoveryFactory);
    }

    private void Publish(string code, string message, DiagnosticSeverity severity, string? source)
        => m_diagnostics.Publish(new Diagnostic(code, message, severity, source));

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_stopping || m_retirementStage != 0)
            throw new InvalidOperationException("Audio retirement has started; no new work can be accepted.");
        m_extensions.EnsureHealthy();
    }
}
