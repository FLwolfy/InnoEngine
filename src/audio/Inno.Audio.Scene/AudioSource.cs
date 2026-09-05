using System;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;
using Inno.Scene;

namespace Inno.Audio.Scene;

/// <summary>
/// Declares a scene-owned audio emitter whose state is synchronized by the official Scene provider.
/// </summary>
[StableTypeId("ee1ef744-40b4-4ea1-b4a9-ea9d907f274f")]
public sealed class AudioSource : GameBehavior
{
    [SerializableProperty]
    private string m_bus = AudioBusId.master.value;

    private ulong m_playbackRevision;
    private bool m_playRequested;
    private float m_volume = 1f;
    private float m_pitch = 1f;
    private float m_pan;
    private float m_minDistance = 1f;
    private float m_maxDistance = 100f;
    private float m_rolloff = 1f;
    private float m_coneInnerAngle = 360f;
    private float m_coneOuterAngle = 360f;
    private float m_coneOuterGain = 1f;
    private float m_dopplerFactor = 1f;

    /// <summary>
    /// Gets or sets the imported clip played by this emitter.
    /// </summary>
    [SerializableProperty]
    public AudioClipAsset? clip { get; set; }

    /// <summary>
    /// Gets or sets whether the emitter requests playback during its first active lifecycle.
    /// </summary>
    [SerializableProperty]
    public bool playOnAwake { get; set; } = true;

    /// <summary>
    /// Gets or sets whether playback restarts when the clip reaches its end.
    /// </summary>
    [SerializableProperty]
    public bool loop { get; set; }

    /// <summary>
    /// Gets or sets the non-negative linear source gain.
    /// </summary>
    [SerializableProperty]
    public float volume
    {
        get => m_volume;
        set
        {
            if (value < 0f)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_volume = value;
        }
    }

    /// <summary>
    /// Gets or sets the positive playback-rate multiplier.
    /// </summary>
    [SerializableProperty]
    public float pitch
    {
        get => m_pitch;
        set
        {
            if (value <= 0f)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_pitch = value;
        }
    }

    /// <summary>
    /// Gets or sets stereo pan from negative one to positive one.
    /// </summary>
    [SerializableProperty]
    public float pan
    {
        get => m_pan;
        set
        {
            if (value is < -1f or > 1f)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_pan = value;
        }
    }

    /// <summary>
    /// Gets or sets the voice-stealing retention priority.
    /// </summary>
    [SerializableProperty]
    public int priority { get; set; }

    /// <summary>
    /// Gets or sets the stable destination mixer bus.
    /// </summary>
    public AudioBusId bus
    {
        get => new(m_bus);
        set
        {
            if (!value.isValid)
                throw new ArgumentException("A valid audio bus ID is required.", nameof(value));
            m_bus = value.value;
        }
    }

    /// <summary>
    /// Gets or sets the preferred decoded or streamed clip preparation mode.
    /// </summary>
    [SerializableProperty]
    public AudioClipLoadMode loadMode { get; set; } = AudioClipLoadMode.Automatic;

    /// <summary>
    /// Gets or sets whether this source participates in listener-relative spatialization.
    /// </summary>
    [SerializableProperty]
    public bool spatialize { get; set; }

    /// <summary>
    /// Gets or sets the distance attenuation model used by a spatial source.
    /// </summary>
    [SerializableProperty]
    public AudioDistanceModel distanceModel { get; set; } = AudioDistanceModel.Inverse;

    /// <summary>
    /// Gets or sets the distance at which attenuation begins.
    /// </summary>
    [SerializableProperty]
    public float minDistance
    {
        get => m_minDistance;
        set
        {
            if (value < 0f || value > m_maxDistance)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_minDistance = value;
        }
    }

    /// <summary>
    /// Gets or sets the distance beyond which attenuation is clamped.
    /// </summary>
    [SerializableProperty]
    public float maxDistance
    {
        get => m_maxDistance;
        set
        {
            if (value < m_minDistance)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_maxDistance = value;
        }
    }

    /// <summary>
    /// Gets or sets the non-negative attenuation strength.
    /// </summary>
    [SerializableProperty]
    public float rolloff
    {
        get => m_rolloff;
        set
        {
            if (value < 0f)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_rolloff = value;
        }
    }

    /// <summary>
    /// Gets or sets the full-gain source cone angle in degrees.
    /// </summary>
    [SerializableProperty]
    public float coneInnerAngle
    {
        get => m_coneInnerAngle;
        set
        {
            if (value is < 0f or > 360f || value > m_coneOuterAngle)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_coneInnerAngle = value;
        }
    }

    /// <summary>
    /// Gets or sets the outer source cone angle in degrees.
    /// </summary>
    [SerializableProperty]
    public float coneOuterAngle
    {
        get => m_coneOuterAngle;
        set
        {
            if (value < m_coneInnerAngle || value > 360f)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_coneOuterAngle = value;
        }
    }

    /// <summary>
    /// Gets or sets gain outside the outer source cone.
    /// </summary>
    [SerializableProperty]
    public float coneOuterGain
    {
        get => m_coneOuterGain;
        set
        {
            if (value is < 0f or > 1f)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_coneOuterGain = value;
        }
    }

    /// <summary>
    /// Gets or sets the non-negative Doppler multiplier.
    /// </summary>
    [SerializableProperty]
    public float dopplerFactor
    {
        get => m_dopplerFactor;
        set
        {
            if (value < 0f)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_dopplerFactor = value;
        }
    }

    /// <summary>
    /// Gets whether this component currently requests a retained scene voice.
    /// </summary>
    public bool isPlaybackRequested => m_playRequested;

    /// <summary>
    /// Requests playback or restarts the current clip at the next audio synchronization point.
    /// </summary>
    public void Play()
    {
        m_playRequested = true;
        m_playbackRevision++;
        if (m_playbackRevision == 0)
            m_playbackRevision++;
    }

    /// <summary>
    /// Stops retaining this emitter's scene voice at the next audio synchronization point.
    /// </summary>
    public void Stop() => m_playRequested = false;

    /// <summary>
    /// Requests initial playback when this component awakens and <see cref="playOnAwake"/> is enabled.
    /// </summary>
    protected override void Awake()
    {
        if (playOnAwake)
            Play();
    }

    internal ulong playbackRevision => m_playbackRevision;
}
