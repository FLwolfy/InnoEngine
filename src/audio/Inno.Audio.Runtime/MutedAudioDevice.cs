using System;
using System.Collections.Generic;

namespace Inno.Audio.Runtime;

/// <summary>
/// Advances deterministic audio state without opening an operating-system output device.
/// </summary>
/// <remarks>
/// This device is used for headless tests and explicit no-device recovery. It never claims audible output.
/// </remarks>
public sealed class MutedAudioDevice : AudioDevice, IAudioDevice
{
    private static uint S_NEXT_GENERATION;

    private readonly Dictionary<ulong, AudioClipDescriptor> m_clips = [];
    private readonly Queue<AudioDeviceCompletion> m_completions = [];
    private readonly Dictionary<ulong, ListenerRecord> m_listeners = [];
    private readonly Dictionary<ulong, BusRecord> m_buses = [];
    private readonly Dictionary<ulong, VoiceRecord> m_voices = [];
    private double m_dspTime;
    private ulong m_nextIdentity = 1;
    private bool m_disposed;

    /// <summary>
    /// Creates a muted device with deterministic clock and lifecycle behavior.
    /// </summary>
    /// <param name="sampleRate">
    /// Positive logical output sample rate used for capability reporting.
    /// </param>
    public MutedAudioDevice(int sampleRate = 48000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        generation = unchecked(++S_NEXT_GENERATION);
        if (generation == 0)
            generation = unchecked(++S_NEXT_GENERATION);
        capabilities = new AudioCapabilities(true, true, true, 4, sampleRate);
    }

    /// <summary>
    /// Gets logical capabilities available while output is muted.
    /// </summary>
    public AudioCapabilities capabilities { get; }

    /// <summary>
    /// Gets the non-zero logical device generation.
    /// </summary>
    public uint generation { get; }

    /// <summary>
    /// Gets <see cref="AudioDeviceState.Muted"/> until this device is disposed.
    /// </summary>
    public AudioDeviceState state => m_disposed ? AudioDeviceState.Disposed : AudioDeviceState.Muted;

    /// <summary>
    /// Gets the deterministic logical audio clock in seconds.
    /// </summary>
    public double dspTime => m_dspTime;

    /// <summary>
    /// Gets current logical resource statistics.
    /// </summary>
    public AudioStatistics statistics
        => new(
            ActiveVoiceCount(),
            m_clips.Count,
            0,
            0);

    AudioClipHandle IAudioDevice.CreateClip(AudioClipDescriptor descriptor)
    {
        EnsureActive();
        ulong id = m_nextIdentity++;
        m_clips.Add(id, descriptor);
        return CreateClipHandle(id, generation);
    }

    bool IAudioDevice.DestroyClip(AudioClipHandle clip)
    {
        EnsureActive();
        DeviceHandleIdentity identity = GetHandleIdentity(clip);
        if (identity.generation != generation || HasActiveVoiceForClip(identity.value))
            return false;
        return m_clips.Remove(identity.value);
    }

    AudioVoiceHandle IAudioDevice.Play(
        AudioClipHandle clip,
        AudioBusHandle bus,
        AudioPlayOptions options,
        double? scheduledDspTime)
    {
        EnsureActive();
        DeviceHandleIdentity clipIdentity = GetHandleIdentity(clip);
        DeviceHandleIdentity busIdentity = GetHandleIdentity(bus);
        if (clipIdentity.generation != generation || !m_clips.TryGetValue(clipIdentity.value, out AudioClipDescriptor descriptor))
            return default;
        if (busIdentity.generation != generation || !m_buses.ContainsKey(busIdentity.value))
            return default;
        ulong id = m_nextIdentity++;
        AudioPlaybackState playbackState = scheduledDspTime is double start && start > m_dspTime
            ? AudioPlaybackState.Scheduled
            : AudioPlaybackState.Playing;
        m_voices.Add(id, new VoiceRecord(
            clipIdentity.value,
            descriptor.frameCount / (double)descriptor.sampleRate,
            options,
            playbackState,
            scheduledDspTime ?? m_dspTime));
        return CreateVoiceHandle(id, generation);
    }

    bool IAudioDevice.Stop(AudioVoiceHandle voice)
        => CompleteVoice(voice, AudioCompletionReason.Stopped);

    bool IAudioDevice.Pause(AudioVoiceHandle voice)
    {
        if (!TryGetVoice(voice, out VoiceRecord record) ||
            record.state is AudioPlaybackState.Completed or AudioPlaybackState.Paused)
        {
            return false;
        }
        record.state = AudioPlaybackState.Paused;
        return true;
    }

    bool IAudioDevice.Resume(AudioVoiceHandle voice)
    {
        if (!TryGetVoice(voice, out VoiceRecord record) || record.state != AudioPlaybackState.Paused)
            return false;
        record.state = record.scheduledDspTime > m_dspTime
            ? AudioPlaybackState.Scheduled
            : AudioPlaybackState.Playing;
        return true;
    }

    bool IAudioDevice.Seek(AudioVoiceHandle voice, TimeSpan position)
    {
        if (position < TimeSpan.Zero || !TryGetVoice(voice, out VoiceRecord record) ||
            record.state == AudioPlaybackState.Completed)
        {
            return false;
        }
        record.position = position.TotalSeconds;
        return true;
    }

    bool IAudioDevice.SetVoiceParameters(AudioVoiceHandle voice, AudioVoiceParameters parameters)
    {
        if (!TryGetVoice(voice, out VoiceRecord record) || record.state == AudioPlaybackState.Completed)
            return false;
        record.parameters = parameters;
        return true;
    }

    bool IAudioDevice.TryGetVoiceState(AudioVoiceHandle voice, out AudioPlaybackState playbackState)
    {
        if (TryGetVoice(voice, out VoiceRecord record))
        {
            playbackState = record.state;
            return true;
        }
        playbackState = AudioPlaybackState.Invalid;
        return false;
    }

    AudioBusHandle IAudioDevice.CreateBus(AudioBusId id, AudioBusHandle parent)
    {
        EnsureActive();
        if (!id.isValid)
            return default;
        DeviceHandleIdentity parentIdentity = GetHandleIdentity(parent);
        if (parent.isValid && (parentIdentity.generation != generation || !m_buses.ContainsKey(parentIdentity.value)))
            return default;
        ulong identity = m_nextIdentity++;
        m_buses.Add(identity, new BusRecord(id, parentIdentity.value));
        return CreateBusHandle(identity, generation);
    }

    bool IAudioDevice.DestroyBus(AudioBusHandle bus)
    {
        EnsureActive();
        DeviceHandleIdentity identity = GetHandleIdentity(bus);
        if (identity.generation != generation)
            return false;
        return m_buses.Remove(identity.value);
    }

    bool IAudioDevice.SetBusVolume(AudioBusHandle bus, float volume)
    {
        if (volume < 0f || !TryGetBus(bus, out BusRecord record))
            return false;
        record.volume = volume;
        return true;
    }

    bool IAudioDevice.SetBusMuted(AudioBusHandle bus, bool muted)
    {
        if (!TryGetBus(bus, out BusRecord record))
            return false;
        record.muted = muted;
        return true;
    }

    bool IAudioDevice.SetBusPaused(AudioBusHandle bus, bool paused)
    {
        if (!TryGetBus(bus, out BusRecord record))
            return false;
        record.paused = paused;
        return true;
    }

    bool IAudioDevice.AddBusProcessor(AudioBusHandle bus, AudioProcessorConfiguration processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        return TryGetBus(bus, out _);
    }

    AudioListenerHandle IAudioDevice.CreateListener(AudioListenerState state)
    {
        EnsureActive();
        if (m_listeners.Count >= capabilities.maxListeners)
            return default;
        ulong id = m_nextIdentity++;
        m_listeners.Add(id, new ListenerRecord(state));
        return CreateListenerHandle(id, generation);
    }

    bool IAudioDevice.SetListener(AudioListenerHandle listener, AudioListenerState state)
    {
        DeviceHandleIdentity identity = GetHandleIdentity(listener);
        if (identity.generation != generation || !m_listeners.TryGetValue(identity.value, out ListenerRecord? record))
            return false;
        record.state = state;
        return true;
    }

    bool IAudioDevice.DestroyListener(AudioListenerHandle listener)
    {
        DeviceHandleIdentity identity = GetHandleIdentity(listener);
        return identity.generation == generation && m_listeners.Remove(identity.value);
    }

    void IAudioDevice.Update(float deltaTime)
    {
        EnsureActive();
        if (deltaTime < 0f)
            deltaTime = 0f;
        m_dspTime += deltaTime;
        foreach ((ulong id, VoiceRecord voice) in m_voices)
        {
            if (voice.state == AudioPlaybackState.Scheduled && voice.scheduledDspTime <= m_dspTime)
                voice.state = AudioPlaybackState.Playing;
            if (voice.state != AudioPlaybackState.Playing)
                continue;
            voice.position += deltaTime * voice.parameters.pitch;
            if (!voice.loop && voice.duration > 0d && voice.position >= voice.duration)
            {
                voice.state = AudioPlaybackState.Completed;
                m_completions.Enqueue(new AudioDeviceCompletion(
                    CreateVoiceHandle(id, generation),
                    AudioCompletionReason.NaturalEnd));
            }
            else if (voice.loop && voice.duration > 0d && voice.position >= voice.duration)
            {
                voice.position %= voice.duration;
            }
        }
    }

    bool IAudioDevice.TryDequeueCompletion(out AudioDeviceCompletion completion)
    {
        if (!m_completions.TryDequeue(out completion))
            return false;
        DeviceHandleIdentity identity = GetHandleIdentity(completion.voice);
        if (identity.generation == generation)
            m_voices.Remove(identity.value);
        return true;
    }

    /// <summary>
    /// Releases all logical clips, voices, buses, and listeners.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_clips.Clear();
        m_voices.Clear();
        m_buses.Clear();
        m_listeners.Clear();
        m_completions.Clear();
    }

    private bool CompleteVoice(AudioVoiceHandle voice, AudioCompletionReason reason)
    {
        if (!TryGetVoice(voice, out VoiceRecord record) || record.state == AudioPlaybackState.Completed)
            return false;
        record.state = AudioPlaybackState.Completed;
        m_completions.Enqueue(new AudioDeviceCompletion(voice, reason));
        return true;
    }

    private bool TryGetBus(AudioBusHandle bus, out BusRecord record)
    {
        EnsureActive();
        DeviceHandleIdentity identity = GetHandleIdentity(bus);
        if (identity.generation == generation && m_buses.TryGetValue(identity.value, out BusRecord? found))
        {
            record = found;
            return true;
        }
        record = null!;
        return false;
    }

    private bool TryGetVoice(AudioVoiceHandle voice, out VoiceRecord record)
    {
        EnsureActive();
        DeviceHandleIdentity identity = GetHandleIdentity(voice);
        if (identity.generation == generation && m_voices.TryGetValue(identity.value, out VoiceRecord? found))
        {
            record = found;
            return true;
        }
        record = null!;
        return false;
    }

    private int ActiveVoiceCount()
    {
        int count = 0;
        foreach (VoiceRecord voice in m_voices.Values)
        {
            if (voice.state != AudioPlaybackState.Completed)
                count++;
        }
        return count;
    }

    private bool HasActiveVoiceForClip(ulong clip)
    {
        foreach (VoiceRecord voice in m_voices.Values)
        {
            if (voice.clip == clip && voice.state != AudioPlaybackState.Completed)
                return true;
        }
        return false;
    }

    private void EnsureActive() => ObjectDisposedException.ThrowIf(m_disposed, this);

    private sealed class BusRecord(AudioBusId id, ulong parent)
    {
        internal AudioBusId id { get; } = id;
        internal ulong parent { get; } = parent;
        internal float volume { get; set; } = 1f;
        internal bool muted { get; set; }
        internal bool paused { get; set; }
    }

    private sealed class ListenerRecord(AudioListenerState state)
    {
        internal AudioListenerState state { get; set; } = state;
    }

    private sealed class VoiceRecord
    {
        internal VoiceRecord(
            ulong clip,
            double duration,
            AudioPlayOptions options,
            AudioPlaybackState state,
            double scheduledDspTime)
        {
            this.clip = clip;
            this.duration = duration;
            loop = options.loop;
            parameters = new AudioVoiceParameters(options.volume, options.pitch, options.pan, options.spatial);
            this.state = state;
            this.scheduledDspTime = scheduledDspTime;
        }

        internal ulong clip { get; }
        internal double duration { get; }
        internal bool loop { get; }
        internal double scheduledDspTime { get; }
        internal AudioVoiceParameters parameters { get; set; }
        internal double position { get; set; }
        internal AudioPlaybackState state { get; set; }
    }
}
