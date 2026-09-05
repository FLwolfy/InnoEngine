using System;
using Inno.Core.Mathematics;

namespace Inno.Audio;

/// <summary>
/// Selects how encoded clip data is prepared for playback.
/// </summary>
public enum AudioClipLoadMode
{
    /// <summary>
    /// Lets the runtime choose between decoded and streamed storage.
    /// </summary>
    Automatic,

    /// <summary>
    /// Decodes the complete clip before playback.
    /// </summary>
    Decode,

    /// <summary>
    /// Decodes encoded data incrementally while playback advances.
    /// </summary>
    Stream
}

/// <summary>
/// Describes the availability of the active audio output generation.
/// </summary>
public enum AudioDeviceState
{
    /// <summary>
    /// The device has not completed initialization.
    /// </summary>
    Initializing,

    /// <summary>
    /// The device is available for audible playback.
    /// </summary>
    Ready,

    /// <summary>
    /// Playback is advancing without an output device.
    /// </summary>
    Muted,

    /// <summary>
    /// The active output device was lost and recovery is pending.
    /// </summary>
    Lost,

    /// <summary>
    /// The device has been released and cannot accept work.
    /// </summary>
    Disposed
}

/// <summary>
/// Describes the observable lifecycle of one playback voice.
/// </summary>
public enum AudioPlaybackState
{
    /// <summary>
    /// The voice is waiting for clip preparation.
    /// </summary>
    Preparing,

    /// <summary>
    /// The voice is scheduled against the audio clock.
    /// </summary>
    Scheduled,

    /// <summary>
    /// The voice is actively advancing.
    /// </summary>
    Playing,

    /// <summary>
    /// The voice is paused at its current cursor.
    /// </summary>
    Paused,

    /// <summary>
    /// The voice has completed and no longer accepts control commands.
    /// </summary>
    Completed,

    /// <summary>
    /// The handle is invalid, stale, or unknown to the active runtime.
    /// </summary>
    Invalid
}

/// <summary>
/// Explains why a voice stopped accepting playback commands.
/// </summary>
public enum AudioCompletionReason
{
    /// <summary>
    /// The clip reached its natural end.
    /// </summary>
    NaturalEnd,

    /// <summary>
    /// A caller explicitly stopped the voice.
    /// </summary>
    Stopped,

    /// <summary>
    /// The runtime reclaimed the voice to satisfy its voice budget.
    /// </summary>
    Stolen,

    /// <summary>
    /// Encoded data could not be prepared or decoded.
    /// </summary>
    DecodeFailed,

    /// <summary>
    /// Playback ended because the owning device generation was lost.
    /// </summary>
    DeviceLost
}

/// <summary>
/// Selects the distance attenuation model for one spatial voice.
/// </summary>
public enum AudioDistanceModel
{
    /// <summary>
    /// Disables distance attenuation while retaining spatial direction.
    /// </summary>
    None,

    /// <summary>
    /// Uses inverse-distance attenuation.
    /// </summary>
    Inverse,

    /// <summary>
    /// Uses linear-distance attenuation.
    /// </summary>
    Linear,

    /// <summary>
    /// Uses exponential-distance attenuation.
    /// </summary>
    Exponential
}

/// <summary>
/// Describes backend-neutral features and limits for one device generation.
/// </summary>
public sealed record AudioCapabilities
{
    /// <summary>
    /// Creates an immutable audio capability snapshot.
    /// </summary>
    /// <param name="supportsStreaming">
    /// Whether clips can be decoded incrementally.
    /// </param>
    /// <param name="supportsScheduledPlayback">
    /// Whether voices can start against the audio clock.
    /// </param>
    /// <param name="supportsSpatialAudio">
    /// Whether listener-relative spatial playback is available.
    /// </param>
    /// <param name="maxListeners">
    /// Maximum simultaneously active listeners.
    /// </param>
    /// <param name="sampleRate">
    /// Device output sample rate in frames per second.
    /// </param>
    public AudioCapabilities(
        bool supportsStreaming,
        bool supportsScheduledPlayback,
        bool supportsSpatialAudio,
        int maxListeners,
        int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxListeners);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        this.supportsStreaming = supportsStreaming;
        this.supportsScheduledPlayback = supportsScheduledPlayback;
        this.supportsSpatialAudio = supportsSpatialAudio;
        this.maxListeners = maxListeners;
        this.sampleRate = sampleRate;
    }

    /// <summary>
    /// Gets whether clips can be decoded incrementally.
    /// </summary>
    public bool supportsStreaming { get; }

    /// <summary>
    /// Gets whether voices can start against the audio clock.
    /// </summary>
    public bool supportsScheduledPlayback { get; }

    /// <summary>
    /// Gets whether listener-relative spatial playback is available.
    /// </summary>
    public bool supportsSpatialAudio { get; }

    /// <summary>
    /// Gets the maximum simultaneously active listener count.
    /// </summary>
    public int maxListeners { get; }

    /// <summary>
    /// Gets the output sample rate in frames per second.
    /// </summary>
    public int sampleRate { get; }
}

/// <summary>
/// Reports an immutable snapshot of runtime and backend resource usage.
/// </summary>
public readonly record struct AudioStatistics
{
    /// <summary>
    /// Creates an audio statistics snapshot.
    /// </summary>
    /// <param name="activeVoices">
    /// Voices that currently consume runtime budget.
    /// </param>
    /// <param name="loadedClips">
    /// Clip allocations held by the backend.
    /// </param>
    /// <param name="decodedBytes">
    /// Approximate decoded clip bytes held in memory.
    /// </param>
    /// <param name="stolenVoiceCount">
    /// Voices reclaimed since runtime creation.
    /// </param>
    public AudioStatistics(int activeVoices, int loadedClips, long decodedBytes, long stolenVoiceCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(activeVoices);
        ArgumentOutOfRangeException.ThrowIfNegative(loadedClips);
        ArgumentOutOfRangeException.ThrowIfNegative(decodedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(stolenVoiceCount);
        this.activeVoices = activeVoices;
        this.loadedClips = loadedClips;
        this.decodedBytes = decodedBytes;
        this.stolenVoiceCount = stolenVoiceCount;
    }

    /// <summary>
    /// Gets voices that currently consume runtime budget.
    /// </summary>
    public int activeVoices { get; }

    /// <summary>
    /// Gets clip allocations held by the backend.
    /// </summary>
    public int loadedClips { get; }

    /// <summary>
    /// Gets approximate decoded clip bytes held in memory.
    /// </summary>
    public long decodedBytes { get; }

    /// <summary>
    /// Gets voices reclaimed since runtime creation.
    /// </summary>
    public long stolenVoiceCount { get; }
}

/// <summary>
/// Contains listener-relative positioning and attenuation parameters for one voice.
/// </summary>
public readonly record struct AudioSpatialOptions
{
    /// <summary>
    /// Creates spatial playback parameters.
    /// </summary>
    /// <param name="position">
    /// World-space source position.
    /// </param>
    /// <param name="direction">
    /// World-space source forward direction.
    /// </param>
    /// <param name="velocity">
    /// World-space source velocity used for Doppler calculation.
    /// </param>
    /// <param name="distanceModel">
    /// Distance attenuation model.
    /// </param>
    /// <param name="minDistance">
    /// Distance at which attenuation begins.
    /// </param>
    /// <param name="maxDistance">
    /// Distance beyond which attenuation is clamped.
    /// </param>
    /// <param name="rolloff">
    /// Attenuation strength for the selected distance model.
    /// </param>
    /// <param name="coneInnerAngle">
    /// Full-volume source cone angle in degrees.
    /// </param>
    /// <param name="coneOuterAngle">
    /// Outer source cone angle in degrees.
    /// </param>
    /// <param name="coneOuterGain">
    /// Gain outside the outer cone.
    /// </param>
    /// <param name="dopplerFactor">
    /// Doppler effect multiplier.
    /// </param>
    public AudioSpatialOptions(
        Vector3 position,
        Vector3 direction,
        Vector3 velocity,
        AudioDistanceModel distanceModel = AudioDistanceModel.Inverse,
        float minDistance = 1f,
        float maxDistance = 100f,
        float rolloff = 1f,
        float coneInnerAngle = 360f,
        float coneOuterAngle = 360f,
        float coneOuterGain = 1f,
        float dopplerFactor = 1f)
    {
        if (minDistance < 0f || maxDistance < minDistance)
            throw new ArgumentOutOfRangeException(nameof(minDistance), "Spatial distance bounds are invalid.");
        if (rolloff < 0f)
            throw new ArgumentOutOfRangeException(nameof(rolloff));
        if (coneInnerAngle is < 0f or > 360f || coneOuterAngle < coneInnerAngle || coneOuterAngle > 360f)
            throw new ArgumentOutOfRangeException(nameof(coneOuterAngle), "Spatial cone angles are invalid.");
        if (coneOuterGain is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(coneOuterGain));
        if (dopplerFactor < 0f)
            throw new ArgumentOutOfRangeException(nameof(dopplerFactor));
        this.position = position;
        this.direction = direction;
        this.velocity = velocity;
        this.distanceModel = distanceModel;
        this.minDistance = minDistance;
        this.maxDistance = maxDistance;
        this.rolloff = rolloff;
        this.coneInnerAngle = coneInnerAngle;
        this.coneOuterAngle = coneOuterAngle;
        this.coneOuterGain = coneOuterGain;
        this.dopplerFactor = dopplerFactor;
    }

    /// <summary>
    /// Gets the world-space source position.
    /// </summary>
    public Vector3 position { get; }

    /// <summary>
    /// Gets the world-space source forward direction.
    /// </summary>
    public Vector3 direction { get; }

    /// <summary>
    /// Gets the world-space source velocity.
    /// </summary>
    public Vector3 velocity { get; }

    /// <summary>
    /// Gets the distance attenuation model.
    /// </summary>
    public AudioDistanceModel distanceModel { get; }

    /// <summary>
    /// Gets the distance at which attenuation begins.
    /// </summary>
    public float minDistance { get; }

    /// <summary>
    /// Gets the distance beyond which attenuation is clamped.
    /// </summary>
    public float maxDistance { get; }

    /// <summary>
    /// Gets the distance attenuation strength.
    /// </summary>
    public float rolloff { get; }

    /// <summary>
    /// Gets the full-volume cone angle in degrees.
    /// </summary>
    public float coneInnerAngle { get; }

    /// <summary>
    /// Gets the outer cone angle in degrees.
    /// </summary>
    public float coneOuterAngle { get; }

    /// <summary>
    /// Gets the gain outside the outer cone.
    /// </summary>
    public float coneOuterGain { get; }

    /// <summary>
    /// Gets the Doppler effect multiplier.
    /// </summary>
    public float dopplerFactor { get; }
}

/// <summary>
/// Contains immutable parameters used to create one playback voice.
/// </summary>
public readonly record struct AudioPlayOptions
{
    /// <summary>
    /// Creates default playback parameters.
    /// </summary>
    public AudioPlayOptions()
        : this(1f, 1f, 0f, false, 0, AudioBusId.master, AudioClipLoadMode.Automatic, null)
    {
    }

    /// <summary>
    /// Creates playback parameters.
    /// </summary>
    /// <param name="volume">
    /// Linear gain applied to the voice.
    /// </param>
    /// <param name="pitch">
    /// Playback-rate multiplier.
    /// </param>
    /// <param name="pan">
    /// Stereo pan from negative one to positive one.
    /// </param>
    /// <param name="loop">
    /// Whether playback restarts after reaching the clip end.
    /// </param>
    /// <param name="priority">
    /// Voice retention priority; larger values are retained first.
    /// </param>
    /// <param name="bus">
    /// Destination mixer bus.
    /// </param>
    /// <param name="loadMode">
    /// Clip preparation override.
    /// </param>
    /// <param name="spatial">
    /// Optional spatial playback parameters.
    /// </param>
    public AudioPlayOptions(
        float volume = 1f,
        float pitch = 1f,
        float pan = 0f,
        bool loop = false,
        int priority = 0,
        AudioBusId? bus = null,
        AudioClipLoadMode loadMode = AudioClipLoadMode.Automatic,
        AudioSpatialOptions? spatial = null)
    {
        if (volume < 0f)
            throw new ArgumentOutOfRangeException(nameof(volume));
        if (pitch <= 0f)
            throw new ArgumentOutOfRangeException(nameof(pitch));
        if (pan is < -1f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(pan));
        AudioBusId resolvedBus = bus ?? AudioBusId.master;
        if (!resolvedBus.isValid)
            throw new ArgumentException("A valid destination bus is required.", nameof(bus));
        this.volume = volume;
        this.pitch = pitch;
        this.pan = pan;
        this.loop = loop;
        this.priority = priority;
        this.bus = resolvedBus;
        this.loadMode = loadMode;
        this.spatial = spatial;
    }

    /// <summary>
    /// Gets the default playback parameters.
    /// </summary>
    public static AudioPlayOptions defaultValue { get; } = new();

    /// <summary>
    /// Gets the linear voice gain.
    /// </summary>
    public float volume { get; }

    /// <summary>
    /// Gets the playback-rate multiplier.
    /// </summary>
    public float pitch { get; }

    /// <summary>
    /// Gets the stereo pan value.
    /// </summary>
    public float pan { get; }

    /// <summary>
    /// Gets whether playback loops at the clip end.
    /// </summary>
    public bool loop { get; }

    /// <summary>
    /// Gets the voice retention priority.
    /// </summary>
    public int priority { get; }

    /// <summary>
    /// Gets the destination mixer bus.
    /// </summary>
    public AudioBusId bus { get; }

    /// <summary>
    /// Gets the clip preparation override.
    /// </summary>
    public AudioClipLoadMode loadMode { get; }

    /// <summary>
    /// Gets optional spatial playback parameters.
    /// </summary>
    public AudioSpatialOptions? spatial { get; }
}

/// <summary>
/// Contains mutable voice parameters that can be changed after playback begins.
/// </summary>
public readonly record struct AudioVoiceParameters
{
    /// <summary>
    /// Creates a voice parameter snapshot.
    /// </summary>
    /// <param name="volume">
    /// Linear voice gain.
    /// </param>
    /// <param name="pitch">
    /// Playback-rate multiplier.
    /// </param>
    /// <param name="pan">
    /// Stereo pan from negative one to positive one.
    /// </param>
    /// <param name="spatial">
    /// Optional spatial playback parameters.
    /// </param>
    public AudioVoiceParameters(float volume, float pitch, float pan, AudioSpatialOptions? spatial = null)
    {
        if (volume < 0f)
            throw new ArgumentOutOfRangeException(nameof(volume));
        if (pitch <= 0f)
            throw new ArgumentOutOfRangeException(nameof(pitch));
        if (pan is < -1f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(pan));
        this.volume = volume;
        this.pitch = pitch;
        this.pan = pan;
        this.spatial = spatial;
    }

    /// <summary>
    /// Gets the linear voice gain.
    /// </summary>
    public float volume { get; }

    /// <summary>
    /// Gets the playback-rate multiplier.
    /// </summary>
    public float pitch { get; }

    /// <summary>
    /// Gets the stereo pan value.
    /// </summary>
    public float pan { get; }

    /// <summary>
    /// Gets optional spatial playback parameters.
    /// </summary>
    public AudioSpatialOptions? spatial { get; }
}

/// <summary>
/// Contains one listener transform used by backend spatialization.
/// </summary>
public readonly record struct AudioListenerState
{
    /// <summary>
    /// Creates a listener transform snapshot.
    /// </summary>
    /// <param name="position">
    /// World-space listener position.
    /// </param>
    /// <param name="direction">
    /// World-space listener forward direction.
    /// </param>
    /// <param name="up">
    /// World-space listener up direction.
    /// </param>
    /// <param name="velocity">
    /// World-space listener velocity.
    /// </param>
    public AudioListenerState(Vector3 position, Vector3 direction, Vector3 up, Vector3 velocity)
    {
        this.position = position;
        this.direction = direction;
        this.up = up;
        this.velocity = velocity;
    }

    /// <summary>
    /// Gets the world-space listener position.
    /// </summary>
    public Vector3 position { get; }

    /// <summary>
    /// Gets the world-space listener forward direction.
    /// </summary>
    public Vector3 direction { get; }

    /// <summary>
    /// Gets the world-space listener up direction.
    /// </summary>
    public Vector3 up { get; }

    /// <summary>
    /// Gets the world-space listener velocity.
    /// </summary>
    public Vector3 velocity { get; }
}
