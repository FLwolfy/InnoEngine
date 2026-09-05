using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Inno.Native.MiniAudio;
using NativeApi = Inno.Native.MiniAudio.MiniAudio;

namespace Inno.Audio.MiniAudio;

/// <summary>
/// Implements the backend-neutral audio device contract with a private MiniAudio engine and node graph.
/// </summary>
public sealed unsafe class MiniAudioDevice : AudioDevice, IAudioDevice
{
    private const uint C_PROCESS_BLOCK_FRAMES = 4096;

    private static int S_NEXT_GENERATION;

    private readonly Dictionary<ulong, BusRecord> m_buses = [];
    private readonly Dictionary<ulong, AudioClipDescriptor> m_clips = [];
    private readonly Queue<AudioDeviceCompletion> m_completions = [];
    private readonly Dictionary<ulong, uint> m_listeners = [];
    private readonly Dictionary<ulong, VoiceRecord> m_voices = [];
    private readonly MiniAudioDeviceOptions m_options;
    private readonly MaEngine* m_engine;
    private readonly float* m_processBuffer;

    private AudioDeviceState m_state;
    private double m_pendingProcessFrames;
    private ulong m_nextIdentity = 1;
    private bool m_engineInitialized;

    /// <summary>
    /// Creates a MiniAudio device using the default operating-system output device.
    /// </summary>
    public MiniAudioDevice()
        : this(new MiniAudioDeviceOptions())
    {
    }

    /// <summary>
    /// Creates one MiniAudio backend generation with explicit output and headless settings.
    /// </summary>
    /// <param name="options">
    /// Device graph and operating-system output settings.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the native engine cannot be initialized with the requested settings.
    /// </exception>
    public MiniAudioDevice(MiniAudioDeviceOptions options)
    {
        m_options = (options ?? throw new ArgumentNullException(nameof(options))).Validate();
        generation = NextGeneration();
        m_state = AudioDeviceState.Initializing;
        m_engine = (MaEngine*)NativeMemory.AllocZeroed((nuint)sizeof(MaEngine));

        try
        {
            MaEngineConfig config = NativeApi.EngineConfigInit();
            config.Channels = checked((uint)m_options.channels);
            config.SampleRate = checked((uint)m_options.sampleRate);
            config.ListenerCount = checked((uint)m_options.listenerCount);
            config.NoDevice = m_options.noDevice ? 1u : 0u;
            MaResult result = NativeApi.EngineInit(in config, new MaEnginePtr(m_engine));
            if (result != MaResult.Success)
                throw new InvalidOperationException($"MiniAudio engine initialization failed with result '{result}'.");

            m_engineInitialized = true;
            int actualSampleRate = checked((int)NativeApi.EngineGetSampleRate(new MaEnginePtr(m_engine)));
            capabilities = new AudioCapabilities(true, true, true, m_options.listenerCount, actualSampleRate);
            if (m_options.noDevice)
            {
                m_processBuffer = (float*)NativeMemory.AllocZeroed(
                    checked((nuint)(C_PROCESS_BLOCK_FRAMES * (uint)m_options.channels * sizeof(float))));
                m_state = AudioDeviceState.Muted;
            }
            else
            {
                m_state = AudioDeviceState.Ready;
            }
        }
        catch
        {
            if (m_engineInitialized)
                NativeApi.EngineUninit(new MaEnginePtr(m_engine));
            NativeMemory.Free(m_engine);
            throw;
        }
    }

    /// <summary>
    /// Gets MiniAudio capabilities for this immutable device generation.
    /// </summary>
    public AudioCapabilities capabilities { get; }

    /// <summary>
    /// Gets the non-zero generation encoded into every handle created by this device.
    /// </summary>
    public uint generation { get; }

    /// <summary>
    /// Gets current native output availability without exposing a MiniAudio device type.
    /// </summary>
    public AudioDeviceState state => m_state;

    /// <summary>
    /// Gets the monotonic MiniAudio engine clock in seconds.
    /// </summary>
    public double dspTime
        => m_engineInitialized
            ? NativeApi.EngineGetTimeInPcmFrames(new MaEnginePtr(m_engine)) / (double)capabilities.sampleRate
            : 0d;

    /// <summary>
    /// Gets current native resource counts and approximate decoded storage.
    /// </summary>
    public AudioStatistics statistics
        => new(
            m_voices.Count,
            m_clips.Count,
            m_clips.Values
                .Where(static descriptor => descriptor.loadMode == AudioClipLoadMode.Decode)
                .Sum(EstimateDecodedByteLength),
            0);

    AudioClipHandle IAudioDevice.CreateClip(AudioClipDescriptor descriptor)
    {
        EnsureActive();
        if (!System.IO.File.Exists(descriptor.artifactPath))
            return default;
        ulong identity = m_nextIdentity++;
        m_clips.Add(identity, descriptor);
        return CreateClipHandle(identity, generation);
    }

    bool IAudioDevice.DestroyClip(AudioClipHandle clip)
    {
        EnsureActive();
        DeviceHandleIdentity identity = GetHandleIdentity(clip);
        if (identity.generation != generation ||
            m_voices.Values.Any(voice => voice.clipIdentity == identity.value))
        {
            return false;
        }
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
        if (busIdentity.generation != generation || !m_buses.TryGetValue(busIdentity.value, out BusRecord? busRecord))
            return default;
        if (scheduledDspTime is double startTime &&
            (double.IsNaN(startTime) || double.IsInfinity(startTime) || startTime < 0d))
        {
            return default;
        }

        MaSound* sound = (MaSound*)NativeMemory.AllocZeroed((nuint)sizeof(MaSound));
        MaSoundPtr soundPointer = new(sound);
        uint flags = descriptor.loadMode == AudioClipLoadMode.Stream
            ? (uint)MaSoundFlags.FlagStream
            : (uint)MaSoundFlags.FlagDecode;
        MaResult initResult = NativeApi.SoundInitFromFile(
            new MaEnginePtr(m_engine),
            descriptor.artifactPath,
            flags,
            new MaSoundPtr(busRecord.group),
            MaFencePtr.Null,
            soundPointer);
        if (initResult != MaResult.Success)
        {
            NativeMemory.Free(sound);
            return default;
        }

        try
        {
            ApplyVoiceParameters(soundPointer, new AudioVoiceParameters(options.volume, options.pitch, options.pan, options.spatial));
            NativeApi.SoundSetLooping(soundPointer, options.loop ? 1u : 0u);
            AudioPlaybackState playbackState = AudioPlaybackState.Playing;
            if (scheduledDspTime is double scheduled && scheduled > dspTime)
            {
                NativeApi.SoundSetStartTimeInPcmFrames(
                    soundPointer,
                    checked((ulong)Math.Round(scheduled * capabilities.sampleRate)));
                playbackState = AudioPlaybackState.Scheduled;
            }

            MaResult startResult = NativeApi.SoundStart(soundPointer);
            if (startResult != MaResult.Success)
            {
                NativeApi.SoundUninit(soundPointer);
                NativeMemory.Free(sound);
                return default;
            }

            ulong identity = m_nextIdentity++;
            m_voices.Add(identity, new VoiceRecord(clipIdentity.value, busRecord, sound, playbackState, scheduledDspTime));
            return CreateVoiceHandle(identity, generation);
        }
        catch
        {
            NativeApi.SoundUninit(soundPointer);
            NativeMemory.Free(sound);
            throw;
        }
    }

    bool IAudioDevice.Stop(AudioVoiceHandle voice)
    {
        if (!TryGetVoice(voice, out ulong identity, out VoiceRecord record))
            return false;
        NativeApi.SoundStop(new MaSoundPtr(record.sound));
        CompleteVoice(identity, record, AudioCompletionReason.Stopped);
        return true;
    }

    bool IAudioDevice.Pause(AudioVoiceHandle voice)
    {
        if (!TryGetVoice(voice, out _, out VoiceRecord record) || record.state == AudioPlaybackState.Paused)
            return false;
        if (NativeApi.SoundStop(new MaSoundPtr(record.sound)) != MaResult.Success)
            return false;
        record.state = AudioPlaybackState.Paused;
        return true;
    }

    bool IAudioDevice.Resume(AudioVoiceHandle voice)
    {
        if (!TryGetVoice(voice, out _, out VoiceRecord record) || record.state != AudioPlaybackState.Paused)
            return false;
        if (NativeApi.SoundStart(new MaSoundPtr(record.sound)) != MaResult.Success)
            return false;
        record.state = record.scheduledDspTime is double scheduled && scheduled > dspTime
            ? AudioPlaybackState.Scheduled
            : AudioPlaybackState.Playing;
        return true;
    }

    bool IAudioDevice.Seek(AudioVoiceHandle voice, TimeSpan position)
    {
        if (position < TimeSpan.Zero || !TryGetVoice(voice, out _, out VoiceRecord record))
            return false;
        ulong frame = checked((ulong)Math.Round(position.TotalSeconds * capabilities.sampleRate));
        return NativeApi.SoundSeekToPcmFrame(new MaSoundPtr(record.sound), frame) == MaResult.Success;
    }

    bool IAudioDevice.SetVoiceParameters(AudioVoiceHandle voice, AudioVoiceParameters parameters)
    {
        if (!TryGetVoice(voice, out _, out VoiceRecord record))
            return false;
        ApplyVoiceParameters(new MaSoundPtr(record.sound), parameters);
        return true;
    }

    bool IAudioDevice.TryGetVoiceState(AudioVoiceHandle voice, out AudioPlaybackState playbackState)
    {
        if (TryGetVoice(voice, out _, out VoiceRecord record))
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

        BusRecord? parentRecord = null;
        if (parent.isValid)
        {
            DeviceHandleIdentity parentIdentity = GetHandleIdentity(parent);
            if (parentIdentity.generation != generation || !m_buses.TryGetValue(parentIdentity.value, out parentRecord))
                return default;
        }
        else if (id != AudioBusId.master)
        {
            return default;
        }

        MaSound* group = (MaSound*)NativeMemory.AllocZeroed((nuint)sizeof(MaSound));
        MaResult result = NativeApi.SoundGroupInit(
            new MaEnginePtr(m_engine),
            0,
            parentRecord is null ? MaSoundPtr.Null : new MaSoundPtr(parentRecord.group),
            new MaSoundPtr(group));
        if (result != MaResult.Success)
        {
            NativeMemory.Free(group);
            return default;
        }

        ulong identity = m_nextIdentity++;
        void* target = parentRecord is null
            ? NativeApi.EngineGetEndpoint(new MaEnginePtr(m_engine))
            : parentRecord.group;
        m_buses.Add(identity, new BusRecord(id, parentRecord, group, target));
        return CreateBusHandle(identity, generation);
    }

    bool IAudioDevice.DestroyBus(AudioBusHandle bus)
    {
        EnsureActive();
        if (!TryGetBus(bus, out ulong identity, out BusRecord record))
            return false;
        if (m_buses.Values.Any(candidate => candidate.parent == record) ||
            m_voices.Values.Any(voice => voice.bus == record))
        {
            return false;
        }
        ReleaseBus(record);
        m_buses.Remove(identity);
        return true;
    }

    bool IAudioDevice.SetBusVolume(AudioBusHandle bus, float volume)
    {
        if (volume < 0f || !TryGetBus(bus, out _, out BusRecord record))
            return false;
        record.volume = volume;
        ApplyBusVolume(record);
        return true;
    }

    bool IAudioDevice.SetBusMuted(AudioBusHandle bus, bool muted)
    {
        if (!TryGetBus(bus, out _, out BusRecord record))
            return false;
        record.muted = muted;
        ApplyBusVolume(record);
        return true;
    }

    bool IAudioDevice.SetBusPaused(AudioBusHandle bus, bool paused)
    {
        if (!TryGetBus(bus, out _, out BusRecord record))
            return false;
        if (record.paused == paused)
            return true;
        MaResult result = paused
            ? NativeApi.SoundGroupStop(new MaSoundPtr(record.group))
            : NativeApi.SoundGroupStart(new MaSoundPtr(record.group));
        if (result != MaResult.Success)
            return false;
        record.paused = paused;
        return true;
    }

    bool IAudioDevice.AddBusProcessor(AudioBusHandle bus, AudioProcessorConfiguration processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        if (!TryGetBus(bus, out _, out BusRecord record))
            return false;
        NativeProcessor? nativeProcessor = CreateProcessor(processor);
        if (nativeProcessor is null)
            return false;

        if (NativeApi.NodeDetachOutputBus(record.tail, 0) != MaResult.Success ||
            NativeApi.NodeAttachOutputBus(record.tail, 0, nativeProcessor.node, 0) != MaResult.Success ||
            NativeApi.NodeAttachOutputBus(nativeProcessor.node, 0, record.target, 0) != MaResult.Success)
        {
            NativeApi.NodeDetachOutputBus(record.tail, 0);
            NativeApi.NodeDetachOutputBus(nativeProcessor.node, 0);
            NativeApi.NodeAttachOutputBus(record.tail, 0, record.target, 0);
            ReleaseProcessor(nativeProcessor);
            return false;
        }

        record.processors.Add(nativeProcessor);
        record.tail = nativeProcessor.node;
        return true;
    }

    AudioListenerHandle IAudioDevice.CreateListener(AudioListenerState state)
    {
        EnsureActive();
        if (m_listeners.Count >= capabilities.maxListeners)
            return default;
        uint listenerIndex = Enumerable.Range(0, capabilities.maxListeners)
            .Select(static index => checked((uint)index))
            .First(index => !m_listeners.ContainsValue(index));
        ulong identity = m_nextIdentity++;
        m_listeners.Add(identity, listenerIndex);
        ApplyListener(listenerIndex, state, true);
        return CreateListenerHandle(identity, generation);
    }

    bool IAudioDevice.SetListener(AudioListenerHandle listener, AudioListenerState state)
    {
        DeviceHandleIdentity identity = GetHandleIdentity(listener);
        if (identity.generation != generation || !m_listeners.TryGetValue(identity.value, out uint listenerIndex))
            return false;
        ApplyListener(listenerIndex, state, true);
        return true;
    }

    bool IAudioDevice.DestroyListener(AudioListenerHandle listener)
    {
        DeviceHandleIdentity identity = GetHandleIdentity(listener);
        if (identity.generation != generation || !m_listeners.Remove(identity.value, out uint listenerIndex))
            return false;
        NativeApi.EngineListenerSetEnabled(new MaEnginePtr(m_engine), listenerIndex, 0);
        return true;
    }

    void IAudioDevice.Update(float deltaTime)
    {
        if (deltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        EnsureActive();
        if (m_options.noDevice)
            ProcessHeadlessFrames(deltaTime);
        else
            UpdateOutputDeviceState();

        if (m_state == AudioDeviceState.Lost)
            return;

        double currentTime = dspTime;
        foreach ((ulong identity, VoiceRecord record) in m_voices.ToArray())
        {
            if (record.state == AudioPlaybackState.Scheduled &&
                record.scheduledDspTime is double scheduled && scheduled <= currentTime)
            {
                record.state = AudioPlaybackState.Playing;
            }
            if (NativeApi.SoundAtEnd(new MaSoundPtr(record.sound)) != 0)
                CompleteVoice(identity, record, AudioCompletionReason.NaturalEnd);
        }
    }

    bool IAudioDevice.TryDequeueCompletion(out AudioDeviceCompletion completion)
        => m_completions.TryDequeue(out completion);

    /// <summary>
    /// Releases all sounds, buses, processor nodes, listeners, and the native engine in dependency order.
    /// </summary>
    public void Dispose()
    {
        if (m_state == AudioDeviceState.Disposed)
            return;

        foreach (VoiceRecord voice in m_voices.Values.ToArray())
            ReleaseVoice(voice);
        m_voices.Clear();

        foreach (BusRecord bus in m_buses.Values.OrderByDescending(GetBusDepth).ToArray())
            ReleaseBus(bus);
        m_buses.Clear();

        if (m_engineInitialized)
        {
            NativeApi.EngineUninit(new MaEnginePtr(m_engine));
            m_engineInitialized = false;
        }
        NativeMemory.Free(m_processBuffer);
        NativeMemory.Free(m_engine);
        m_state = AudioDeviceState.Disposed;
        GC.SuppressFinalize(this);
    }

    private static uint NextGeneration()
    {
        uint generation = unchecked((uint)Interlocked.Increment(ref S_NEXT_GENERATION));
        return generation == 0
            ? unchecked((uint)Interlocked.Increment(ref S_NEXT_GENERATION))
            : generation;
    }

    private static long EstimateDecodedByteLength(AudioClipDescriptor descriptor)
    {
        try
        {
            long decoded = checked(descriptor.frameCount * descriptor.channels * sizeof(float));
            return Math.Max(descriptor.encodedByteLength, decoded);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private static int GetBusDepth(BusRecord bus)
    {
        int depth = 0;
        for (BusRecord? current = bus.parent; current is not null; current = current.parent)
            depth++;
        return depth;
    }

    private static float GetParameter(
        AudioProcessorConfiguration processor,
        AudioParameterId id,
        float defaultValue)
    {
        foreach (AudioProcessorParameter parameter in processor.parameters)
        {
            if (parameter.id == id)
                return parameter.value;
        }
        return defaultValue;
    }

    private static MaAttenuationModel ToNativeDistanceModel(AudioDistanceModel model)
        => model switch
        {
            AudioDistanceModel.None => MaAttenuationModel.None,
            AudioDistanceModel.Inverse => MaAttenuationModel.Inverse,
            AudioDistanceModel.Linear => MaAttenuationModel.Linear,
            AudioDistanceModel.Exponential => MaAttenuationModel.Exponential,
            _ => throw new ArgumentOutOfRangeException(nameof(model))
        };

    private static void ApplyVoiceParameters(MaSoundPtr sound, AudioVoiceParameters parameters)
    {
        NativeApi.SoundSetVolume(sound, parameters.volume);
        NativeApi.SoundSetPitch(sound, parameters.pitch);
        NativeApi.SoundSetPan(sound, parameters.pan);
        if (parameters.spatial is not AudioSpatialOptions spatial)
        {
            NativeApi.SoundSetSpatializationEnabled(sound, 0);
            return;
        }

        NativeApi.SoundSetSpatializationEnabled(sound, 1);
        NativeApi.SoundSetPositioning(sound, MaPositioning.Absolute);
        NativeApi.SoundSetPosition(sound, spatial.position.x, spatial.position.y, spatial.position.z);
        NativeApi.SoundSetDirection(sound, spatial.direction.x, spatial.direction.y, spatial.direction.z);
        NativeApi.SoundSetVelocity(sound, spatial.velocity.x, spatial.velocity.y, spatial.velocity.z);
        NativeApi.SoundSetAttenuationModel(sound, ToNativeDistanceModel(spatial.distanceModel));
        NativeApi.SoundSetMinDistance(sound, spatial.minDistance);
        NativeApi.SoundSetMaxDistance(sound, spatial.maxDistance);
        NativeApi.SoundSetRolloff(sound, spatial.rolloff);
        NativeApi.SoundSetCone(
            sound,
            spatial.coneInnerAngle * (MathF.PI / 180f),
            spatial.coneOuterAngle * (MathF.PI / 180f),
            spatial.coneOuterGain);
        NativeApi.SoundSetDopplerFactor(sound, spatial.dopplerFactor);
    }

    private void ApplyListener(uint index, AudioListenerState state, bool enabled)
    {
        MaEnginePtr engine = new(m_engine);
        NativeApi.EngineListenerSetPosition(engine, index, state.position.x, state.position.y, state.position.z);
        NativeApi.EngineListenerSetDirection(engine, index, state.direction.x, state.direction.y, state.direction.z);
        NativeApi.EngineListenerSetWorldUp(engine, index, state.up.x, state.up.y, state.up.z);
        NativeApi.EngineListenerSetVelocity(engine, index, state.velocity.x, state.velocity.y, state.velocity.z);
        NativeApi.EngineListenerSetEnabled(engine, index, enabled ? 1u : 0u);
    }

    private void ApplyBusVolume(BusRecord bus)
        => NativeApi.SoundGroupSetVolume(new MaSoundPtr(bus.group), bus.muted ? 0f : bus.volume);

    private NativeProcessor? CreateProcessor(AudioProcessorConfiguration processor)
    {
        if (processor.id == AudioProcessorId.delay)
        {
            float delayMilliseconds = Math.Max(0f, GetParameter(processor, AudioParameterId.delayMilliseconds, 250f));
            float decay = Math.Clamp(GetParameter(processor, AudioParameterId.decay, 0.35f), 0f, 1f);
            uint delayFrames = checked((uint)Math.Round(delayMilliseconds * capabilities.sampleRate / 1000d));
            MaDelayNode* node = (MaDelayNode*)NativeMemory.AllocZeroed((nuint)sizeof(MaDelayNode));
            MaDelayNodeConfig config = NativeApi.DelayNodeConfigInit(
                checked((uint)m_options.channels),
                checked((uint)capabilities.sampleRate),
                delayFrames,
                decay);
            MaResult result = NativeApi.DelayNodeInit(
                NativeApi.EngineGetNodeGraph(new MaEnginePtr(m_engine)),
                in config,
                MaAllocationCallbacksPtr.Null,
                new MaDelayNodePtr(node));
            if (result != MaResult.Success)
            {
                NativeMemory.Free(node);
                return null;
            }
            return new NativeProcessor(node, true);
        }

        if (processor.id != AudioProcessorId.lowPass &&
            processor.id != AudioProcessorId.highPass &&
            processor.id != AudioProcessorId.bandPass &&
            processor.id != AudioProcessorId.notch &&
            processor.id != AudioProcessorId.peak &&
            processor.id != AudioProcessorId.lowShelf &&
            processor.id != AudioProcessorId.highShelf)
        {
            return null;
        }

        BiquadCoefficients coefficients = CreateBiquadCoefficients(processor);
        MaBiquadNode* biquad = (MaBiquadNode*)NativeMemory.AllocZeroed((nuint)sizeof(MaBiquadNode));
        MaBiquadNodeConfig biquadConfig = NativeApi.BiquadNodeConfigInit(
            checked((uint)m_options.channels),
            coefficients.b0,
            coefficients.b1,
            coefficients.b2,
            coefficients.a0,
            coefficients.a1,
            coefficients.a2);
        MaResult biquadResult = NativeApi.BiquadNodeInit(
            NativeApi.EngineGetNodeGraph(new MaEnginePtr(m_engine)),
            in biquadConfig,
            MaAllocationCallbacksPtr.Null,
            new MaBiquadNodePtr(biquad));
        if (biquadResult != MaResult.Success)
        {
            NativeMemory.Free(biquad);
            return null;
        }
        return new NativeProcessor(biquad, false);
    }

    private BiquadCoefficients CreateBiquadCoefficients(AudioProcessorConfiguration processor)
    {
        double frequency = Math.Clamp(
            GetParameter(processor, AudioParameterId.frequency, processor.id == AudioProcessorId.lowPass ? 20000f : 200f),
            1f,
            capabilities.sampleRate * 0.499f);
        double quality = Math.Max(0.001, GetParameter(processor, AudioParameterId.quality, 0.70710678f));
        double gain = GetParameter(processor, AudioParameterId.gainDecibels, 0f);
        double shelfSlope = Math.Max(0.001, GetParameter(processor, AudioParameterId.shelfSlope, 1f));
        double omega = 2d * Math.PI * frequency / capabilities.sampleRate;
        double sine = Math.Sin(omega);
        double cosine = Math.Cos(omega);
        double alpha = sine / (2d * quality);

        if (processor.id == AudioProcessorId.lowPass)
            return new((1d - cosine) / 2d, 1d - cosine, (1d - cosine) / 2d, 1d + alpha, -2d * cosine, 1d - alpha);
        if (processor.id == AudioProcessorId.highPass)
            return new((1d + cosine) / 2d, -(1d + cosine), (1d + cosine) / 2d, 1d + alpha, -2d * cosine, 1d - alpha);
        if (processor.id == AudioProcessorId.bandPass)
            return new(alpha, 0d, -alpha, 1d + alpha, -2d * cosine, 1d - alpha);
        if (processor.id == AudioProcessorId.notch)
            return new(1d, -2d * cosine, 1d, 1d + alpha, -2d * cosine, 1d - alpha);

        double amplitude = Math.Pow(10d, gain / 40d);
        if (processor.id == AudioProcessorId.peak)
            return new(1d + alpha * amplitude, -2d * cosine, 1d - alpha * amplitude, 1d + alpha / amplitude, -2d * cosine, 1d - alpha / amplitude);

        double shelfAlpha = sine / 2d * Math.Sqrt((amplitude + 1d / amplitude) * (1d / shelfSlope - 1d) + 2d);
        double twoRootAAlpha = 2d * Math.Sqrt(amplitude) * shelfAlpha;
        if (processor.id == AudioProcessorId.lowShelf)
        {
            return new(
                amplitude * ((amplitude + 1d) - (amplitude - 1d) * cosine + twoRootAAlpha),
                2d * amplitude * ((amplitude - 1d) - (amplitude + 1d) * cosine),
                amplitude * ((amplitude + 1d) - (amplitude - 1d) * cosine - twoRootAAlpha),
                (amplitude + 1d) + (amplitude - 1d) * cosine + twoRootAAlpha,
                -2d * ((amplitude - 1d) + (amplitude + 1d) * cosine),
                (amplitude + 1d) + (amplitude - 1d) * cosine - twoRootAAlpha);
        }
        return new(
            amplitude * ((amplitude + 1d) + (amplitude - 1d) * cosine + twoRootAAlpha),
            -2d * amplitude * ((amplitude - 1d) + (amplitude + 1d) * cosine),
            amplitude * ((amplitude + 1d) + (amplitude - 1d) * cosine - twoRootAAlpha),
            (amplitude + 1d) - (amplitude - 1d) * cosine + twoRootAAlpha,
            2d * ((amplitude - 1d) - (amplitude + 1d) * cosine),
            (amplitude + 1d) - (amplitude - 1d) * cosine - twoRootAAlpha);
    }

    private void ProcessHeadlessFrames(float deltaTime)
    {
        m_pendingProcessFrames += deltaTime * capabilities.sampleRate;
        while (m_pendingProcessFrames >= 1d)
        {
            ulong requested = (ulong)Math.Min(Math.Floor(m_pendingProcessFrames), C_PROCESS_BLOCK_FRAMES);
            ulong read = 0;
            MaResult result = NativeApi.EngineReadPcmFrames(
                new MaEnginePtr(m_engine),
                (nint)m_processBuffer,
                requested,
                ref read);
            if (result != MaResult.Success)
            {
                m_state = AudioDeviceState.Lost;
                foreach ((ulong identity, VoiceRecord voice) in m_voices.ToArray())
                    CompleteVoice(identity, voice, AudioCompletionReason.DeviceLost);
                return;
            }
            m_pendingProcessFrames -= read;
            if (read == 0)
                return;
        }
    }

    private void UpdateOutputDeviceState()
    {
        MaDevicePtr device = NativeApi.EngineGetDevice(new MaEnginePtr(m_engine));
        if (device.IsNull)
        {
            m_state = AudioDeviceState.Lost;
            return;
        }
        MaDeviceState state = NativeApi.DeviceGetState(device);
        if (state is MaDeviceState.Started or MaDeviceState.Starting)
        {
            m_state = AudioDeviceState.Ready;
            return;
        }
        if (state == MaDeviceState.Stopped && NativeApi.EngineStart(new MaEnginePtr(m_engine)) == MaResult.Success)
        {
            m_state = AudioDeviceState.Ready;
            return;
        }
        m_state = AudioDeviceState.Lost;
        foreach ((ulong identity, VoiceRecord voice) in m_voices.ToArray())
            CompleteVoice(identity, voice, AudioCompletionReason.DeviceLost);
    }

    private bool TryGetVoice(AudioVoiceHandle handle, out ulong identity, out VoiceRecord record)
    {
        DeviceHandleIdentity decoded = GetHandleIdentity(handle);
        identity = decoded.value;
        if (decoded.generation == generation && m_voices.TryGetValue(decoded.value, out VoiceRecord? found))
        {
            record = found;
            return true;
        }
        record = null!;
        return false;
    }

    private bool TryGetBus(AudioBusHandle handle, out ulong identity, out BusRecord record)
    {
        DeviceHandleIdentity decoded = GetHandleIdentity(handle);
        identity = decoded.value;
        if (decoded.generation == generation && m_buses.TryGetValue(decoded.value, out BusRecord? found))
        {
            record = found;
            return true;
        }
        record = null!;
        return false;
    }

    private void CompleteVoice(ulong identity, VoiceRecord record, AudioCompletionReason reason)
    {
        AudioVoiceHandle handle = CreateVoiceHandle(identity, generation);
        m_voices.Remove(identity);
        ReleaseVoice(record);
        m_completions.Enqueue(new AudioDeviceCompletion(handle, reason));
    }

    private static void ReleaseVoice(VoiceRecord record)
    {
        NativeApi.SoundUninit(new MaSoundPtr(record.sound));
        NativeMemory.Free(record.sound);
    }

    private void ReleaseBus(BusRecord bus)
    {
        NativeApi.SoundGroupUninit(new MaSoundPtr(bus.group));
        for (int index = bus.processors.Count - 1; index >= 0; index--)
            ReleaseProcessor(bus.processors[index]);
        NativeMemory.Free(bus.group);
    }

    private static void ReleaseProcessor(NativeProcessor processor)
    {
        if (processor.isDelay)
            NativeApi.DelayNodeUninit(new MaDelayNodePtr((MaDelayNode*)processor.node), MaAllocationCallbacksPtr.Null);
        else
            NativeApi.BiquadNodeUninit(new MaBiquadNodePtr((MaBiquadNode*)processor.node), MaAllocationCallbacksPtr.Null);
        NativeMemory.Free(processor.node);
    }

    private void EnsureActive()
    {
        if (m_state == AudioDeviceState.Disposed)
            throw new ObjectDisposedException(nameof(MiniAudioDevice));
    }

    private sealed class BusRecord
    {
        internal readonly MaSound* group;
        internal readonly AudioBusId id;
        internal readonly BusRecord? parent;
        internal readonly List<NativeProcessor> processors = [];
        internal readonly void* target;
        internal bool muted;
        internal bool paused;
        internal void* tail;
        internal float volume = 1f;

        internal BusRecord(AudioBusId id, BusRecord? parent, MaSound* group, void* target)
        {
            this.id = id;
            this.parent = parent;
            this.group = group;
            this.target = target;
            tail = group;
        }
    }

    private sealed class VoiceRecord
    {
        internal readonly ulong clipIdentity;
        internal readonly MaSound* sound;
        internal readonly double? scheduledDspTime;
        internal AudioPlaybackState state;
        internal readonly BusRecord bus;

        internal VoiceRecord(
            ulong clipIdentity,
            BusRecord bus,
            MaSound* sound,
            AudioPlaybackState state,
            double? scheduledDspTime)
        {
            this.clipIdentity = clipIdentity;
            this.bus = bus;
            this.sound = sound;
            this.state = state;
            this.scheduledDspTime = scheduledDspTime;
        }
    }

    private sealed class NativeProcessor
    {
        internal readonly bool isDelay;
        internal readonly void* node;

        internal NativeProcessor(void* node, bool isDelay)
        {
            this.node = node;
            this.isDelay = isDelay;
        }
    }

    private readonly record struct BiquadCoefficients
    {
        internal readonly float b0;
        internal readonly float b1;
        internal readonly float b2;
        internal readonly float a0;
        internal readonly float a1;
        internal readonly float a2;

        internal BiquadCoefficients(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            this.b0 = checked((float)b0);
            this.b1 = checked((float)b1);
            this.b2 = checked((float)b2);
            this.a0 = checked((float)a0);
            this.a1 = checked((float)a1);
            this.a2 = checked((float)a2);
        }
    }
}
