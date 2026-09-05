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
public sealed class AudioRuntimeLayer : AudioDevice, IAudioService, IDisposable
{
    private readonly IAssetArtifactLookup m_artifacts;
    private readonly Func<AudioContentScope>? m_contentScopeProvider;
    private readonly Func<IAudioDevice>? m_deviceRecoveryFactory;
    private readonly IAudioDiagnosticSink m_diagnostics;
    private readonly EventDispatcher m_events;
    private readonly AudioExtensionRegistry m_extensions;
    private readonly Dictionary<ClipCacheKey, ClipCacheEntry> m_clips = [];
    private readonly Dictionary<Guid, EmitterRecord> m_emitters = [];
    private readonly Dictionary<AudioBusId, AudioBusHandle> m_buses = [];
    private readonly Dictionary<AudioBusId, BusControlState> m_busControls = [];
    private readonly List<IReadOnlyDictionary<AudioBusId, AudioBusHandle>> m_retiredBuses = [];
    private readonly HashSet<AudioVoiceHandle> m_terminalVoices = [];
    private readonly Queue<AudioVoiceHandle> m_terminalVoiceOrder = [];
    private readonly Dictionary<AudioVoiceHandle, VoiceRecord> m_voices = [];
    private readonly AudioRuntimeOptions m_options;

    private IAudioDevice m_device;
    private AudioMixer m_activeMixer = new AudioMixerBuilder().Build();
    private AudioExtensionRegistry.ProviderGeneration? m_providers;
    private AudioListenerHandle m_listener;
    private Guid m_listenerId;
    private long m_stolenVoiceCount;
    private ulong m_nextVoiceIdentity = 1;
    private long m_nextVoiceSequence;
    private float m_deviceRecoveryElapsed;
    private bool m_deviceLossReported;
    private bool m_disposed;

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
    /// Optional structured diagnostic sink.
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
    public AudioRuntimeLayer(
        TypeCatalog types,
        IAudioDevice device,
        IAssetArtifactLookup artifacts,
        EventDispatcher events,
        IAudioDiagnosticSink? diagnostics = null,
        AudioRuntimeOptions? options = null,
        Func<AudioContentScope>? contentScopeProvider = null,
        Func<IAudioDevice>? deviceRecoveryFactory = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        m_device = device ?? throw new ArgumentNullException(nameof(device));
        m_artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        m_events = events ?? throw new ArgumentNullException(nameof(events));
        m_diagnostics = diagnostics ?? NullDiagnosticSink.instance;
        m_options = (options ?? new AudioRuntimeOptions()).Validate();
        m_contentScopeProvider = contentScopeProvider;
        m_deviceRecoveryFactory = deviceRecoveryFactory;
        m_extensions = new AudioExtensionRegistry(types);
        InstallMixer(m_activeMixer);
        if (m_device.state == AudioDeviceState.Muted)
        {
            Publish(
                "AUDIO_NO_DEVICE",
                "Audio is running in explicit muted mode because no output device is active.",
                AudioDiagnosticSeverity.Warning,
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
    /// Gets current runtime resource statistics.
    /// </summary>
    public AudioStatistics statistics
        => new(
            m_voices.Count,
            m_clips.Count,
            m_clips.Where(static pair => pair.Key.loadMode == AudioClipLoadMode.Decode)
                .Sum(static pair => pair.Value.decodedByteLength),
            m_stolenVoiceCount);

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
    public AudioVoiceHandle Play(AudioClipAsset clip, AudioPlayOptions options)
        => QueueVoice(clip, options, null);

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
        return QueueVoice(clip, options, scheduledDspTime);
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
        if (!m_voices.TryGetValue(voice, out VoiceRecord? record))
            return false;
        if (!record.backendVoice.isValid)
        {
            FinishVoice(record, AudioCompletionReason.Stopped);
            return true;
        }
        record.completionOverride = AudioCompletionReason.Stopped;
        if (m_device.Stop(record.backendVoice))
            return true;
        FinishVoice(record, AudioCompletionReason.Stopped);
        return true;
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
        if (!m_voices.TryGetValue(voice, out VoiceRecord? record))
            return false;
        record.pauseRequested = true;
        if (!record.backendVoice.isValid)
        {
            record.state = AudioPlaybackState.Paused;
            return true;
        }
        bool result = m_device.Pause(record.backendVoice);
        if (result)
            record.state = AudioPlaybackState.Paused;
        return result;
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
        if (!m_voices.TryGetValue(voice, out VoiceRecord? record) || !record.pauseRequested)
            return false;
        record.pauseRequested = false;
        if (!record.backendVoice.isValid)
        {
            record.state = AudioPlaybackState.Preparing;
            return true;
        }
        bool result = m_device.Resume(record.backendVoice);
        if (result)
            UpdateVoiceState(record);
        return result;
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
        if (position < TimeSpan.Zero || !m_voices.TryGetValue(voice, out VoiceRecord? record))
            return false;
        record.seekPosition = position;
        return !record.backendVoice.isValid || m_device.Seek(record.backendVoice, position);
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
        if (!m_voices.TryGetValue(voice, out VoiceRecord? record))
            return false;
        record.parameters = parameters;
        return !record.backendVoice.isValid || m_device.SetVoiceParameters(record.backendVoice, parameters);
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
        if (m_voices.TryGetValue(voice, out VoiceRecord? record))
        {
            UpdateVoiceState(record);
            playbackState = record.state;
            return true;
        }
        if (m_terminalVoices.Contains(voice))
        {
            playbackState = AudioPlaybackState.Completed;
            return true;
        }
        playbackState = AudioPlaybackState.Invalid;
        return false;
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
        if (volume < 0f || !float.IsFinite(volume) ||
            !m_buses.TryGetValue(bus, out AudioBusHandle handle) ||
            !m_device.SetBusVolume(handle, volume))
        {
            return false;
        }
        m_busControls[bus].volume = volume;
        return true;
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
        if (!m_buses.TryGetValue(bus, out AudioBusHandle handle) || !m_device.SetBusMuted(handle, muted))
            return false;
        m_busControls[bus].muted = muted;
        return true;
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
        if (!m_buses.TryGetValue(bus, out AudioBusHandle handle) || !m_device.SetBusPaused(handle, paused))
            return false;
        m_busControls[bus].paused = paused;
        return true;
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
    /// A completed operation after backend preparation succeeds.
    /// </returns>
    public ValueTask PreloadAsync(
        AudioClipAsset clip,
        AudioClipLoadMode loadMode = AudioClipLoadMode.Automatic,
        CancellationToken cancellationToken = default)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(clip);
        cancellationToken.ThrowIfCancellationRequested();
        ClipCacheEntry entry = GetOrCreateClip(clip, loadMode);
        entry.preloadReferences++;
        return ValueTask.CompletedTask;
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
        ArgumentNullException.ThrowIfNull(clip);
        ClipCacheEntry? entry = m_clips
            .Where(pair => pair.Key.persistentId == clip.identity.persistentId &&
                           pair.Key.contentVersion == clip.contentVersion &&
                           pair.Value.preloadReferences > 0)
            .Select(static pair => pair.Value)
            .FirstOrDefault();
        if (entry is null)
            return;
        entry.preloadReferences--;
        TryReleaseClip(entry);
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
        try
        {
            if (!m_extensions.extensions.TryBuildMixer(asset, out AudioMixer? mixer) || mixer is null)
            {
                Publish(
                    "AUDIO_MIXER_EXTENSION_MISSING",
                    "The candidate audio mixer references an unavailable extension; the last-good mixer remains active.",
                    AudioDiagnosticSeverity.Error,
                    asset.mixerTypeId);
                return false;
            }
            InstallMixer(mixer);
            return true;
        }
        catch (Exception exception)
        {
            Publish(
                "AUDIO_MIXER_CANDIDATE_FAILED",
                $"The candidate audio mixer failed validation; the last-good mixer remains active: {exception.Message}",
                AudioDiagnosticSeverity.Error,
                asset.mixerTypeId);
            return false;
        }
    }

    /// <summary>
    /// Replaces a lost or muted backend at a main-thread safety point.
    /// </summary>
    /// <param name="replacement">
    /// Initialized replacement device whose ownership transfers to this runtime.
    /// </param>
    public void ReplaceDevice(IAudioDevice replacement)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(replacement);
        if (ReferenceEquals(replacement, m_device))
            throw new ArgumentException("The replacement must be a different device instance.", nameof(replacement));
        IReadOnlyDictionary<AudioBusId, AudioBusHandle> replacementBuses;
        try
        {
            replacementBuses = CreateBusSet(replacement, m_activeMixer, m_busControls);
        }
        catch
        {
            throw;
        }

        CompleteAll(AudioCompletionReason.DeviceLost);
        ReleaseAllClips();
        DestroyBusSet(m_device, m_buses);
        foreach (IReadOnlyDictionary<AudioBusId, AudioBusHandle> retired in m_retiredBuses)
            DestroyBusSet(m_device, retired);
        m_retiredBuses.Clear();
        if (m_listener.isValid)
            _ = m_device.DestroyListener(m_listener);
        m_device.Dispose();
        m_device = replacement;
        m_buses.Clear();
        foreach ((AudioBusId id, AudioBusHandle handle) in replacementBuses)
            m_buses.Add(id, handle);
        m_listener = default;
        m_listenerId = Guid.Empty;
        m_deviceRecoveryElapsed = 0f;
        m_deviceLossReported = false;
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
                string? source = candidate.GetType().FullName;
                candidate.Dispose();
                candidate = null;
                Publish(
                    "AUDIO_DEVICE_RECOVERY_FAILED",
                    "The replacement audio device did not reach the ready state; muted playback remains active.",
                    AudioDiagnosticSeverity.Warning,
                    source);
                return false;
            }
            string? recoveredSource = candidate.GetType().FullName;
            ReplaceDevice(candidate);
            candidate = null;
            Publish(
                "AUDIO_DEVICE_RECOVERED",
                "The audio output device recovered with a new generation.",
                AudioDiagnosticSeverity.Info,
                recoveredSource);
            return true;
        }
        catch (Exception exception)
        {
            candidate?.Dispose();
            Publish(
                "AUDIO_DEVICE_RECOVERY_FAILED",
                $"The audio output recovery candidate failed; muted playback remains active: {exception.Message}",
                AudioDiagnosticSeverity.Warning,
                candidate?.GetType().FullName ?? "AudioRuntimeLayer");
            return false;
        }
    }

    /// <summary>
    /// Advances providers, pending preparation, backend maintenance, and completion dispatch at a main-thread safety point.
    /// </summary>
    /// <param name="deltaTime">
    /// Non-negative elapsed frame time in seconds.
    /// </param>
    public void Update(float deltaTime)
    {
        EnsureActive();
        if (deltaTime < 0f)
            deltaTime = 0f;
        EnsureProviders();
        CollectContent(deltaTime);
        PrepareVoices();
        m_device.Update(deltaTime);
        DrainCompletions();
        foreach (VoiceRecord voice in m_voices.Values)
            UpdateVoiceState(voice);
        if (m_voices.Count == 0 && m_retiredBuses.Count > 0)
        {
            foreach (IReadOnlyDictionary<AudioBusId, AudioBusHandle> retired in m_retiredBuses)
                DestroyBusSet(m_device, retired);
            m_retiredBuses.Clear();
        }
        if (m_device.state == AudioDeviceState.Lost)
        {
            if (!m_deviceLossReported)
            {
                m_deviceLossReported = true;
                Publish(
                    "AUDIO_DEVICE_LOST",
                    "The audio output device was lost; active voices are completing and recovery will retry at safe points.",
                    AudioDiagnosticSeverity.Warning,
                    m_device.GetType().FullName);
            }
            CompleteAll(AudioCompletionReason.DeviceLost);
        }
        AdvanceDeviceRecovery(deltaTime);
    }

    /// <summary>
    /// Stops voices and releases providers, cache entries, mixer generations, and the owned backend device.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        CompleteAll(AudioCompletionReason.Stopped);
        m_disposed = true;
        m_providers?.Dispose();
        m_extensions.Dispose();
        ReleaseAllClips();
        if (m_listener.isValid)
            _ = m_device.DestroyListener(m_listener);
        DestroyBusSet(m_device, m_buses);
        foreach (IReadOnlyDictionary<AudioBusId, AudioBusHandle> retired in m_retiredBuses)
            DestroyBusSet(m_device, retired);
        m_device.Dispose();
    }

    private AudioVoiceHandle QueueVoice(AudioClipAsset clip, AudioPlayOptions options, double? scheduledDspTime)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(clip);
        while (m_voices.Count >= m_options.maxVoices)
            StealVoice();
        AudioVoiceHandle handle = CreateVoiceHandle(m_nextVoiceIdentity++, m_device.generation);
        var record = new VoiceRecord(
            handle,
            clip,
            options,
            scheduledDspTime,
            m_nextVoiceSequence++);
        m_voices.Add(handle, record);
        return handle;
    }

    private void StealVoice()
    {
        VoiceRecord victim = m_voices.Values
            .OrderBy(static voice => voice.options.priority)
            .ThenBy(static voice => voice.sequence)
            .First();
        if (victim.backendVoice.isValid)
            _ = m_device.Stop(victim.backendVoice);
        m_stolenVoiceCount++;
        FinishVoice(victim, AudioCompletionReason.Stolen);
    }

    private void PrepareVoices()
    {
        foreach (VoiceRecord voice in m_voices.Values.Where(static voice => !voice.backendVoice.isValid).ToArray())
        {
            try
            {
                ClipCacheEntry clip = GetOrCreateClip(voice.clip, voice.options.loadMode);
                if (!m_buses.TryGetValue(voice.options.bus, out AudioBusHandle bus))
                    throw new InvalidOperationException($"Audio bus '{voice.options.bus}' is not present in the active mixer.");
                clip.voiceReferences++;
                voice.clipCache = clip;
                voice.backendVoice = m_device.Play(clip.handle, bus, voice.options, voice.scheduledDspTime);
                if (!voice.backendVoice.isValid)
                    throw new InvalidOperationException("The audio backend rejected voice creation.");
                if (voice.seekPosition is TimeSpan seek)
                    _ = m_device.Seek(voice.backendVoice, seek);
                _ = m_device.SetVoiceParameters(voice.backendVoice, voice.parameters);
                if (voice.pauseRequested)
                    _ = m_device.Pause(voice.backendVoice);
                UpdateVoiceState(voice);
            }
            catch (Exception exception)
            {
                Publish(
                    "AUDIO_CLIP_PREPARATION_FAILED",
                    $"Audio clip '{voice.clip.assetPath}' could not be prepared: {exception.Message}",
                    AudioDiagnosticSeverity.Error,
                    voice.clip.identity.persistentId.ToString("D"));
                FinishVoice(voice, AudioCompletionReason.DecodeFailed);
            }
        }
    }

    private ClipCacheEntry GetOrCreateClip(AudioClipAsset clip, AudioClipLoadMode requestedMode)
    {
        if (clip.isMissing)
            throw new InvalidOperationException("A missing audio clip cannot be prepared.");
        AudioClipMetadata metadata = clip.metadata
            ?? throw new InvalidOperationException("The audio clip has no imported runtime metadata.");
        if (!m_artifacts.TryGetArtifact(clip.identity.persistentId, "audio-data", out AssetArtifactInfo? artifact) ||
            artifact is null)
        {
            throw new InvalidOperationException("The audio-data artifact is unavailable.");
        }
        AudioClipLoadMode resolvedMode = requestedMode == AudioClipLoadMode.Automatic
            ? (artifact.length >= m_options.automaticStreamingThresholdBytes ||
               WouldExceedDecodedBudget(EstimateDecodedByteLength(metadata, artifact.length))
                ? AudioClipLoadMode.Stream
                : AudioClipLoadMode.Decode)
            : requestedMode;
        var key = new ClipCacheKey(clip.identity.persistentId, clip.contentVersion, resolvedMode);
        if (m_clips.TryGetValue(key, out ClipCacheEntry? cached))
            return cached;
        long decodedByteLength = resolvedMode == AudioClipLoadMode.Decode
            ? EstimateDecodedByteLength(metadata, artifact.length)
            : 0;
        if (resolvedMode == AudioClipLoadMode.Decode && WouldExceedDecodedBudget(decodedByteLength))
        {
            throw new InvalidOperationException(
                $"Decoded audio clip '{clip.assetPath}' exceeds the configured cache budget.");
        }
        AudioClipHandle handle = m_device.CreateClip(new AudioClipDescriptor(
            artifact.absolutePath,
            metadata.codec,
            resolvedMode,
            metadata.channels,
            metadata.sampleRate,
            metadata.frameCount,
            artifact.length));
        if (!handle.isValid)
            throw new InvalidOperationException("The audio backend rejected clip creation.");
        var entry = new ClipCacheEntry(key, handle, decodedByteLength);
        m_clips.Add(key, entry);
        return entry;
    }

    private void FinishVoice(VoiceRecord voice, AudioCompletionReason reason)
    {
        if (!m_voices.Remove(voice.handle))
            return;
        if (m_terminalVoices.Add(voice.handle))
            m_terminalVoiceOrder.Enqueue(voice.handle);
        int terminalCapacity = m_options.maxVoices > int.MaxValue / 2
            ? int.MaxValue
            : Math.Max(64, m_options.maxVoices * 2);
        while (m_terminalVoiceOrder.Count > terminalCapacity)
            m_terminalVoices.Remove(m_terminalVoiceOrder.Dequeue());
        if (voice.clipCache is not null)
        {
            voice.clipCache.voiceReferences--;
            TryReleaseClip(voice.clipCache);
        }
        m_events.Enqueue(new AudioVoiceCompletedEvent(voice.handle, reason));
    }

    private void TryReleaseClip(ClipCacheEntry entry)
    {
        if (entry.preloadReferences > 0 || entry.voiceReferences > 0)
            return;
        _ = m_device.DestroyClip(entry.handle);
        m_clips.Remove(entry.key);
    }

    private void DrainCompletions()
    {
        while (m_device.TryDequeueCompletion(out AudioDeviceCompletion completion))
        {
            VoiceRecord? voice = m_voices.Values.FirstOrDefault(candidate => candidate.backendVoice == completion.voice);
            if (voice is null)
                continue;
            FinishVoice(voice, voice.completionOverride ?? completion.reason);
        }
    }

    private void UpdateVoiceState(VoiceRecord voice)
    {
        if (!voice.backendVoice.isValid)
            return;
        if (m_device.TryGetVoiceState(voice.backendVoice, out AudioPlaybackState state))
            voice.state = state;
    }

    private void EnsureProviders()
    {
        try
        {
            AudioExtensionRegistry.Snapshot snapshot = m_extensions.extensions;
            if (m_providers?.typeCacheVersion == snapshot.typeCacheVersion)
                return;
            AudioExtensionRegistry.ProviderGeneration candidate = snapshot.CreateProviders();
            AudioExtensionRegistry.ProviderGeneration? previous = m_providers;
            m_providers = candidate;
            previous?.Dispose();
        }
        catch (Exception exception)
        {
            Publish(
                "AUDIO_PROVIDER_CANDIDATE_FAILED",
                $"Audio content providers could not activate; the last-good generation remains active: {exception.Message}",
                AudioDiagnosticSeverity.Error,
                "AudioRuntimeLayer");
        }
    }

    private void CollectContent(float deltaTime)
    {
        if (m_providers is null)
            return;
        AudioContentScope content;
        try
        {
            content = m_contentScopeProvider?.Invoke() ?? AudioContentScope.empty;
        }
        catch (Exception exception)
        {
            Publish(
                "AUDIO_CONTENT_SCOPE_FAILED",
                $"The host audio content scope failed; an empty scope is used for this update: {exception.Message}",
                AudioDiagnosticSeverity.Error,
                "AudioRuntimeLayer");
            content = AudioContentScope.empty;
        }
        var context = new AudioContentProviderContext(content, deltaTime);
        foreach (AudioExtensionRegistry.ProviderEntry entry in m_providers.providers)
        {
            try
            {
                entry.provider.Submit(context);
            }
            catch (Exception exception)
            {
                Publish(
                    "AUDIO_CONTENT_PROVIDER_FAILED",
                    $"Audio content provider '{entry.id}' was isolated for this update: {exception.Message}",
                    AudioDiagnosticSeverity.Error,
                    entry.id);
            }
        }
        SynchronizeEmitters(context.emitters);
        SynchronizeListener(context.listeners);
    }

    private void SynchronizeEmitters(IReadOnlyList<AudioEmitterSnapshot> snapshots)
    {
        var seen = new HashSet<Guid>();
        foreach (AudioEmitterSnapshot snapshot in snapshots)
        {
            seen.Add(snapshot.id);
            if (!snapshot.shouldPlay)
            {
                if (m_emitters.Remove(snapshot.id, out EmitterRecord? inactive))
                    _ = Stop(inactive.voice);
                continue;
            }
            if (m_emitters.TryGetValue(snapshot.id, out EmitterRecord? current))
            {
                if (!ReferenceEquals(current.clip, snapshot.clip) ||
                    current.clip.contentVersion != snapshot.clip.contentVersion ||
                    current.playbackRevision != snapshot.playbackRevision)
                {
                    _ = Stop(current.voice);
                    current = new EmitterRecord(
                        snapshot.clip,
                        snapshot.playbackRevision,
                        Play(snapshot.clip, snapshot.options));
                    m_emitters[snapshot.id] = current;
                }
                else
                {
                    _ = SetVoiceParameters(
                        current.voice,
                        new AudioVoiceParameters(
                            snapshot.options.volume,
                            snapshot.options.pitch,
                            snapshot.options.pan,
                            snapshot.options.spatial));
                }
                continue;
            }
            m_emitters.Add(
                snapshot.id,
                new EmitterRecord(snapshot.clip, snapshot.playbackRevision, Play(snapshot.clip, snapshot.options)));
        }
        foreach (Guid id in m_emitters.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            _ = Stop(m_emitters[id].voice);
            m_emitters.Remove(id);
        }
    }

    private void SynchronizeListener(IReadOnlyList<AudioListenerSnapshot> snapshots)
    {
        AudioListenerSnapshot[] active = snapshots
            .Where(static listener => listener.active)
            .OrderByDescending(static listener => listener.priority)
            .ThenBy(static listener => listener.id)
            .ToArray();
        if (active.Length == 0)
        {
            if (m_listener.isValid)
                _ = m_device.DestroyListener(m_listener);
            m_listener = default;
            m_listenerId = Guid.Empty;
            return;
        }
        AudioListenerSnapshot selected = active[0];
        if (active.Length > 1 && active[1].priority == selected.priority)
        {
            Publish(
                "AUDIO_LISTENER_PRIORITY_TIE",
                "Multiple active listeners share the highest priority; stable identity selected the winner.",
                AudioDiagnosticSeverity.Warning,
                selected.id.ToString("D"));
        }
        if (!m_listener.isValid || m_listenerId != selected.id)
        {
            if (m_listener.isValid)
                _ = m_device.DestroyListener(m_listener);
            m_listener = m_device.CreateListener(selected.state);
            m_listenerId = selected.id;
            return;
        }
        _ = m_device.SetListener(m_listener, selected.state);
    }

    private void InstallMixer(AudioMixer mixer)
    {
        IReadOnlyDictionary<AudioBusId, AudioBusHandle> candidate = CreateBusSet(m_device, mixer, null);
        if (m_buses.Count > 0)
            m_retiredBuses.Add(new Dictionary<AudioBusId, AudioBusHandle>(m_buses));
        m_buses.Clear();
        foreach ((AudioBusId id, AudioBusHandle handle) in candidate)
            m_buses.Add(id, handle);
        m_busControls.Clear();
        foreach (AudioBusDefinition bus in mixer.buses)
            m_busControls.Add(bus.id, new BusControlState(bus.volume, bus.muted, paused: false));
        m_activeMixer = mixer;
    }

    private static IReadOnlyDictionary<AudioBusId, AudioBusHandle> CreateBusSet(
        IAudioDevice device,
        AudioMixer mixer,
        IReadOnlyDictionary<AudioBusId, BusControlState>? controls)
    {
        var candidate = new Dictionary<AudioBusId, AudioBusHandle>();
        try
        {
            foreach (AudioBusDefinition bus in mixer.buses)
            {
                AudioBusHandle parent = bus.parent is AudioBusId parentId
                    ? candidate[parentId]
                    : default;
                AudioBusHandle handle = device.CreateBus(bus.id, parent);
                if (!handle.isValid)
                    throw new InvalidOperationException($"The backend rejected audio bus '{bus.id}'.");
                candidate.Add(bus.id, handle);
                BusControlState state = controls is not null && controls.TryGetValue(bus.id, out BusControlState? current)
                    ? current
                    : new BusControlState(bus.volume, bus.muted, paused: false);
                if (!device.SetBusVolume(handle, state.volume) ||
                    !device.SetBusMuted(handle, state.muted) ||
                    state.paused && !device.SetBusPaused(handle, paused: true))
                {
                    throw new InvalidOperationException($"The backend rejected parameters for audio bus '{bus.id}'.");
                }
                foreach (AudioProcessorConfiguration processor in bus.processors)
                {
                    if (!device.AddBusProcessor(handle, processor))
                        throw new InvalidOperationException($"The backend rejected processor '{processor.id}'.");
                }
            }
            return candidate;
        }
        catch
        {
            DestroyBusSet(device, candidate);
            throw;
        }
    }

    private void CompleteAll(AudioCompletionReason reason)
    {
        foreach (VoiceRecord voice in m_voices.Values.ToArray())
        {
            if (voice.backendVoice.isValid)
                _ = m_device.Stop(voice.backendVoice);
            FinishVoice(voice, reason);
        }
        m_emitters.Clear();
    }

    private void ReleaseAllClips()
    {
        foreach (ClipCacheEntry clip in m_clips.Values)
            _ = m_device.DestroyClip(clip.handle);
        m_clips.Clear();
    }

    private bool WouldExceedDecodedBudget(long additionalBytes)
    {
        if (additionalBytes > m_options.decodedCacheBudgetBytes)
            return true;
        long currentBytes = m_clips.Values.Sum(static clip => clip.decodedByteLength);
        return currentBytes > m_options.decodedCacheBudgetBytes - additionalBytes;
    }

    private static long EstimateDecodedByteLength(AudioClipMetadata metadata, long encodedByteLength)
    {
        try
        {
            long decoded = checked(metadata.frameCount * metadata.channels * sizeof(float));
            return Math.Max(encodedByteLength, decoded);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private static void DestroyBusSet(
        IAudioDevice device,
        IReadOnlyDictionary<AudioBusId, AudioBusHandle> buses)
    {
        foreach (AudioBusHandle bus in buses.Values.Reverse())
            _ = device.DestroyBus(bus);
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

    private void Publish(string code, string message, AudioDiagnosticSeverity severity, string? source)
        => m_diagnostics.Publish(new AudioDiagnostic(code, message, severity, source));

    private void EnsureActive() => ObjectDisposedException.ThrowIf(m_disposed, this);

    private readonly record struct ClipCacheKey(
        Guid persistentId,
        long contentVersion,
        AudioClipLoadMode loadMode);

    private sealed class ClipCacheEntry(
        ClipCacheKey key,
        AudioClipHandle handle,
        long decodedByteLength)
    {
        internal ClipCacheKey key { get; } = key;
        internal AudioClipHandle handle { get; } = handle;
        internal long decodedByteLength { get; } = decodedByteLength;
        internal int preloadReferences { get; set; }
        internal int voiceReferences { get; set; }
    }

    private sealed class BusControlState(float volume, bool muted, bool paused)
    {
        internal float volume { get; set; } = volume;
        internal bool muted { get; set; } = muted;
        internal bool paused { get; set; } = paused;
    }

    private sealed class EmitterRecord(
        AudioClipAsset clip,
        ulong playbackRevision,
        AudioVoiceHandle voice)
    {
        internal AudioClipAsset clip { get; } = clip;
        internal ulong playbackRevision { get; } = playbackRevision;
        internal AudioVoiceHandle voice { get; } = voice;
    }

    private sealed class VoiceRecord
    {
        internal VoiceRecord(
            AudioVoiceHandle handle,
            AudioClipAsset clip,
            AudioPlayOptions options,
            double? scheduledDspTime,
            long sequence)
        {
            this.handle = handle;
            this.clip = clip;
            this.options = options;
            this.scheduledDspTime = scheduledDspTime;
            this.sequence = sequence;
            parameters = new AudioVoiceParameters(options.volume, options.pitch, options.pan, options.spatial);
        }

        internal AudioVoiceHandle handle { get; }
        internal AudioClipAsset clip { get; }
        internal AudioPlayOptions options { get; }
        internal double? scheduledDspTime { get; }
        internal long sequence { get; }
        internal AudioVoiceHandle backendVoice { get; set; }
        internal ClipCacheEntry? clipCache { get; set; }
        internal AudioCompletionReason? completionOverride { get; set; }
        internal AudioVoiceParameters parameters { get; set; }
        internal TimeSpan? seekPosition { get; set; }
        internal AudioPlaybackState state { get; set; } = AudioPlaybackState.Preparing;
        internal bool pauseRequested { get; set; }
    }

    private sealed class NullDiagnosticSink : IAudioDiagnosticSink
    {
        internal static NullDiagnosticSink instance { get; } = new();

        /// <summary>
        /// Discards a diagnostic when the host did not provide a sink.
        /// </summary>
        /// <param name="diagnostic">
        /// Structured diagnostic to discard.
        /// </param>
        public void Publish(AudioDiagnostic diagnostic)
        {
        }
    }
}
