using System;

namespace Inno.Audio;

/// <summary>
/// Describes one immutable encoded audio artifact presented to a backend device.
/// </summary>
public readonly record struct AudioClipDescriptor
{
    /// <summary>
    /// Creates a backend clip description.
    /// </summary>
    /// <param name="artifactPath">
    /// Absolute path of the immutable encoded artifact.
    /// </param>
    /// <param name="codec">
    /// Codec protocol used by the artifact.
    /// </param>
    /// <param name="loadMode">
    /// Required decoded or streamed storage strategy.
    /// </param>
    /// <param name="channels">
    /// Encoded channel count.
    /// </param>
    /// <param name="sampleRate">
    /// Encoded sample rate in frames per second.
    /// </param>
    /// <param name="frameCount">
    /// Total decoded frame count when known.
    /// </param>
    /// <param name="encodedByteLength">
    /// Encoded artifact length in bytes.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The load mode is undefined or a channel, rate, frame, or byte count is outside its valid range.
    /// </exception>
    public AudioClipDescriptor(
        string artifactPath,
        AudioCodecId codec,
        AudioClipLoadMode loadMode,
        int channels,
        int sampleRate,
        long frameCount,
        long encodedByteLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        if (!PathValidator.IsFullyQualified(artifactPath))
            throw new ArgumentException("An absolute artifact path is required.", nameof(artifactPath));
        if (!codec.isValid)
            throw new ArgumentException("A valid codec identifier is required.", nameof(codec));
        if (!Enum.IsDefined(loadMode))
            throw new ArgumentOutOfRangeException(nameof(loadMode));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        ArgumentOutOfRangeException.ThrowIfNegative(encodedByteLength);
        this.artifactPath = artifactPath;
        this.codec = codec;
        this.loadMode = loadMode;
        this.channels = channels;
        this.sampleRate = sampleRate;
        this.frameCount = frameCount;
        this.encodedByteLength = encodedByteLength;
    }

    /// <summary>
    /// Gets the absolute immutable artifact path.
    /// </summary>
    public string artifactPath { get; }

    /// <summary>
    /// Gets the encoded codec protocol.
    /// </summary>
    public AudioCodecId codec { get; }

    /// <summary>
    /// Gets the required storage strategy.
    /// </summary>
    public AudioClipLoadMode loadMode { get; }

    /// <summary>
    /// Gets the encoded channel count.
    /// </summary>
    public int channels { get; }

    /// <summary>
    /// Gets the encoded sample rate in frames per second.
    /// </summary>
    public int sampleRate { get; }

    /// <summary>
    /// Gets the total decoded frame count, or zero when unknown.
    /// </summary>
    public long frameCount { get; }

    /// <summary>
    /// Gets the encoded artifact length in bytes.
    /// </summary>
    public long encodedByteLength { get; }

    private static class PathValidator
    {
        internal static bool IsFullyQualified(string path) => System.IO.Path.IsPathFullyQualified(path);
    }
}

/// <summary>
/// Reports one backend-detected terminal voice transition.
/// </summary>
public readonly record struct AudioDeviceCompletion
{
    /// <summary>
    /// Creates a backend voice completion record.
    /// </summary>
    /// <param name="voice">
    /// Voice that reached a terminal state.
    /// </param>
    /// <param name="reason">
    /// Reason playback ended.
    /// </param>
    public AudioDeviceCompletion(AudioDeviceVoiceHandle voice, AudioCompletionReason reason)
    {
        if (!voice.isValid)
            throw new ArgumentException("A valid voice handle is required.", nameof(voice));
        this.voice = voice;
        this.reason = reason;
    }

    /// <summary>
    /// Gets the completed voice handle.
    /// </summary>
    public AudioDeviceVoiceHandle voice { get; }

    /// <summary>
    /// Gets the terminal playback reason.
    /// </summary>
    public AudioCompletionReason reason { get; }
}

/// <summary>
/// Owns one replaceable audio backend generation and all device-side audio objects.
/// </summary>
public interface IAudioDevice : IDisposable
{
    /// <summary>
    /// Queries asynchronous clip preparation without blocking the owner thread.
    /// </summary>
    /// <param name="clip">
    /// A handle created by this device generation.
    /// </param>
    /// <returns>
    /// The preparation state, or Failed for a retired or invalid handle.
    /// </returns>
    AudioClipState GetClipState(AudioClipHandle clip);

    /// <summary>
    /// Gets immutable capabilities for the active device generation.
    /// </summary>
    AudioCapabilities capabilities { get; }

    /// <summary>
    /// Gets the non-zero generation used to reject stale handles.
    /// </summary>
    uint generation { get; }

    /// <summary>
    /// Gets the current output availability state.
    /// </summary>
    AudioDeviceState state { get; }

    /// <summary>
    /// Gets the monotonic backend audio clock in seconds.
    /// </summary>
    double dspTime { get; }

    /// <summary>
    /// Gets current backend resource statistics.
    /// </summary>
    AudioStatistics statistics { get; }

    /// <summary>
    /// Allocates one decoded or streamed clip from an immutable artifact.
    /// </summary>
    /// <param name="descriptor">
    /// Encoded artifact and preparation requirements.
    /// </param>
    /// <returns>
    /// A handle owned by the current device generation.
    /// </returns>
    AudioClipHandle CreateClip(AudioClipDescriptor descriptor);

    /// <summary>
    /// Releases one clip after all voices using it have ended.
    /// </summary>
    /// <param name="clip">
    /// Clip owned by the current device generation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a live clip was released.
    /// </returns>
    bool DestroyClip(AudioClipHandle clip);

    /// <summary>
    /// Creates and starts or schedules one playback voice.
    /// </summary>
    /// <param name="clip">
    /// Prepared clip owned by this device generation.
    /// </param>
    /// <param name="bus">
    /// Destination bus owned by this device generation.
    /// </param>
    /// <param name="options">
    /// Immutable playback parameters.
    /// </param>
    /// <param name="scheduledDspTime">
    /// Absolute audio-clock start time, or <see langword="null"/> for immediate playback.
    /// </param>
    /// <returns>
    /// A new voice handle, or an invalid handle when options are uninitialized, scheduling is invalid, or backend preparation fails.
    /// </returns>
    AudioDeviceVoiceHandle Play(
        AudioClipHandle clip,
        AudioBusHandle bus,
        AudioPlayOptions options,
        double? scheduledDspTime = null);

    /// <summary>
    /// Stops a voice and makes its handle terminal.
    /// </summary>
    /// <param name="voice">
    /// Voice owned by the current device generation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a live voice was stopped.
    /// </returns>
    bool Stop(AudioDeviceVoiceHandle voice);

    /// <summary>
    /// Pauses a live voice.
    /// </summary>
    /// <param name="voice">
    /// Voice owned by the current device generation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the voice entered the paused state.
    /// </returns>
    bool Pause(AudioDeviceVoiceHandle voice);

    /// <summary>
    /// Resumes a paused voice.
    /// </summary>
    /// <param name="voice">
    /// Voice owned by the current device generation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the voice resumed.
    /// </returns>
    bool Resume(AudioDeviceVoiceHandle voice);

    /// <summary>
    /// Moves a live voice cursor to a clip-relative position.
    /// </summary>
    /// <param name="voice">
    /// Voice owned by the current device generation.
    /// </param>
    /// <param name="position">
    /// Non-negative clip-relative position.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the cursor was updated.
    /// </returns>
    bool Seek(AudioDeviceVoiceHandle voice, TimeSpan position);

    /// <summary>
    /// Replaces mutable parameters for a live voice.
    /// </summary>
    /// <param name="voice">
    /// Voice owned by the current device generation.
    /// </param>
    /// <param name="parameters">
    /// Current voice parameter snapshot.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a live voice was updated; false for stale handles or uninitialized parameters.
    /// </returns>
    bool SetVoiceParameters(AudioDeviceVoiceHandle voice, AudioVoiceParameters parameters);

    /// <summary>
    /// Queries the current playback state for a voice.
    /// </summary>
    /// <param name="voice">
    /// Voice handle to query.
    /// </param>
    /// <param name="playbackState">
    /// Receives the current state when the handle is known.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the handle belongs to a known voice.
    /// </returns>
    bool TryGetVoiceState(AudioDeviceVoiceHandle voice, out AudioPlaybackState playbackState);

    /// <summary>
    /// Creates one bus routed to a parent bus.
    /// </summary>
    /// <param name="id">
    /// Stable semantic bus identifier.
    /// </param>
    /// <param name="parent">
    /// Parent bus, or an invalid handle when creating the master bus.
    /// </param>
    /// <returns>
    /// A bus handle owned by the current device generation.
    /// </returns>
    AudioBusHandle CreateBus(AudioBusId id, AudioBusHandle parent = default);

    /// <summary>
    /// Releases one graph-generation bus after dependent objects have been removed.
    /// </summary>
    /// <param name="bus">
    /// Bus owned by the current device generation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a live bus was released.
    /// </returns>
    bool DestroyBus(AudioBusHandle bus);

    /// <summary>
    /// Updates linear gain for one bus.
    /// </summary>
    /// <param name="bus">
    /// Bus owned by the current device generation.
    /// </param>
    /// <param name="volume">
    /// Non-negative linear gain.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the bus was updated.
    /// </returns>
    bool SetBusVolume(AudioBusHandle bus, float volume);

    /// <summary>
    /// Updates mute state for one bus.
    /// </summary>
    /// <param name="bus">
    /// Bus owned by the current device generation.
    /// </param>
    /// <param name="muted">
    /// Whether output from the bus is silenced.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the bus was updated.
    /// </returns>
    bool SetBusMuted(AudioBusHandle bus, bool muted);

    /// <summary>
    /// Updates pause state for one bus and its routed voices.
    /// </summary>
    /// <param name="bus">
    /// Bus owned by the current device generation.
    /// </param>
    /// <param name="paused">
    /// Whether routed voices should pause.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the bus was updated.
    /// </returns>
    bool SetBusPaused(AudioBusHandle bus, bool paused);

    /// <summary>
    /// Appends one backend-neutral processor configuration to a bus chain.
    /// </summary>
    /// <param name="bus">
    /// Bus owned by the current device generation.
    /// </param>
    /// <param name="processor">
    /// Open processor protocol and neutral parameter values.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the backend recognized and attached the processor.
    /// </returns>
    bool AddBusProcessor(AudioBusHandle bus, AudioProcessorConfiguration processor);

    /// <summary>
    /// Creates one backend spatial listener.
    /// </summary>
    /// <param name="state">
    /// Initial listener transform.
    /// </param>
    /// <returns>
    /// A listener handle owned by the current device generation.
    /// </returns>
    AudioListenerHandle CreateListener(AudioListenerState state);

    /// <summary>
    /// Updates one backend spatial listener.
    /// </summary>
    /// <param name="listener">
    /// Listener owned by the current device generation.
    /// </param>
    /// <param name="state">
    /// Current listener transform.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the listener was updated.
    /// </returns>
    bool SetListener(AudioListenerHandle listener, AudioListenerState state);

    /// <summary>
    /// Releases one backend spatial listener.
    /// </summary>
    /// <param name="listener">
    /// Listener owned by the current device generation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a live listener was released.
    /// </returns>
    bool DestroyListener(AudioListenerHandle listener);

    /// <summary>
    /// Advances backend maintenance at a main-thread safety point.
    /// </summary>
    /// <param name="deltaTime">
    /// Non-negative elapsed frame time in seconds.
    /// </param>
    void Update(float deltaTime);

    /// <summary>
    /// Tries to consume one terminal voice transition recorded by the backend.
    /// </summary>
    /// <param name="completion">
    /// Receives one terminal transition when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a completion was dequeued.
    /// </returns>
    bool TryDequeueCompletion(out AudioDeviceCompletion completion);
}
