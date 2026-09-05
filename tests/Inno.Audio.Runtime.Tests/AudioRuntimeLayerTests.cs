using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Audio.Runtime;
using Inno.Core.Events;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Xunit;

namespace Inno.Audio.Runtime.Tests;

public sealed class AudioRuntimeLayerTests : IDisposable
{
    private readonly FakeArtifactLookup m_artifacts = new();
    private readonly EventDispatcher m_events = new();
    private readonly string m_root;
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;

    public AudioRuntimeLayerTests()
    {
        m_root = Path.Combine(Path.GetTempPath(), "InnoAudioRuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_root);
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_root, "Assemblies")
        });
        _ = typeof(TestMixerExtension);
        m_types = new TypeCatalog(m_modules);
        m_types.Rebuild();
    }

    public void Dispose()
    {
        m_types.Dispose();
        m_modules.Dispose();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    [Fact]
    public void PlayTransitionsFromPreparingToNaturalCompletionOnMainThread()
    {
        AudioClipAsset clip = CreateClip(frameCount: 4800);
        using var runtime = CreateRuntime();
        using EventHub hub = m_events.CreateHub();
        var completions = new List<AudioVoiceCompletedEvent>();
        hub.Listen<AudioVoiceCompletedEvent>(completions.Add);

        AudioVoiceHandle voice = runtime.Play(clip);
        Assert.True(runtime.TryGetVoiceState(voice, out AudioPlaybackState preparing));
        Assert.Equal(AudioPlaybackState.Preparing, preparing);

        runtime.Update(0.01f);
        Assert.True(runtime.TryGetVoiceState(voice, out AudioPlaybackState playing));
        Assert.Equal(AudioPlaybackState.Playing, playing);
        runtime.Update(0.2f);
        m_events.Flush();

        Assert.True(runtime.TryGetVoiceState(voice, out AudioPlaybackState completed));
        Assert.Equal(AudioPlaybackState.Completed, completed);
        Assert.Single(completions);
        Assert.Equal(AudioCompletionReason.NaturalEnd, completions[0].reason);
    }

    [Fact]
    public void ScheduledLoopingVoiceSupportsPauseResumeAndSeek()
    {
        AudioClipAsset clip = CreateClip(frameCount: 4800);
        using var runtime = CreateRuntime();
        AudioVoiceHandle voice = runtime.PlayScheduled(
            clip,
            1d,
            new AudioPlayOptions(loop: true));

        runtime.Update(0.1f);
        Assert.True(runtime.TryGetVoiceState(voice, out AudioPlaybackState scheduled));
        Assert.Equal(AudioPlaybackState.Scheduled, scheduled);
        Assert.True(runtime.Pause(voice));
        Assert.True(runtime.Seek(voice, TimeSpan.FromMilliseconds(50)));
        Assert.True(runtime.Resume(voice));
        runtime.Update(1f);
        runtime.Update(1f);

        Assert.True(runtime.TryGetVoiceState(voice, out AudioPlaybackState playing));
        Assert.Equal(AudioPlaybackState.Playing, playing);
    }

    [Fact]
    public void VoiceBudgetStealsLowestPriorityThenOldestVoice()
    {
        AudioClipAsset clip = CreateClip(frameCount: 480000);
        using var runtime = CreateRuntime(maxVoices: 2);
        using EventHub hub = m_events.CreateHub();
        var completions = new List<AudioVoiceCompletedEvent>();
        hub.Listen<AudioVoiceCompletedEvent>(completions.Add);
        AudioVoiceHandle first = runtime.Play(clip, new AudioPlayOptions(priority: 1));
        AudioVoiceHandle second = runtime.Play(clip, new AudioPlayOptions(priority: 10));

        AudioVoiceHandle third = runtime.Play(clip, new AudioPlayOptions(priority: 0));
        m_events.Flush();

        Assert.Equal(2, runtime.statistics.activeVoices);
        Assert.True(runtime.TryGetVoiceState(first, out AudioPlaybackState firstState));
        Assert.Equal(AudioPlaybackState.Completed, firstState);
        Assert.True(runtime.TryGetVoiceState(second, out AudioPlaybackState secondState));
        Assert.Equal(AudioPlaybackState.Preparing, secondState);
        Assert.True(runtime.TryGetVoiceState(third, out AudioPlaybackState thirdState));
        Assert.Equal(AudioPlaybackState.Preparing, thirdState);
        Assert.Single(completions);
        Assert.Equal(AudioCompletionReason.Stolen, completions[0].reason);
    }

    [Fact]
    public async Task PreloadRetainsDistinctDecodeAndStreamEntriesUntilReleased()
    {
        AudioClipAsset clip = CreateClip(frameCount: 4800);
        using var runtime = CreateRuntime();

        await runtime.PreloadAsync(clip, AudioClipLoadMode.Decode);
        await runtime.PreloadAsync(clip, AudioClipLoadMode.Stream);
        Assert.Equal(2, runtime.statistics.loadedClips);

        runtime.ReleasePreload(clip);
        runtime.ReleasePreload(clip);
        Assert.Equal(0, runtime.statistics.loadedClips);
    }

    [Fact]
    public async Task AutomaticPreparationStreamsWhenDecodedFootprintExceedsBudget()
    {
        AudioClipAsset clip = CreateClip(frameCount: 4800);
        using var runtime = CreateRuntime(
            decodedCacheBudgetBytes: 1024,
            automaticStreamingThresholdBytes: long.MaxValue);

        await runtime.PreloadAsync(clip);

        Assert.Equal(1, runtime.statistics.loadedClips);
        Assert.Equal(0, runtime.statistics.decodedBytes);
        Assert.Throws<InvalidOperationException>(() => runtime.PreloadAsync(clip, AudioClipLoadMode.Decode));
    }

    [Fact]
    public void MixerExtensionBuildsOpenBusGraphAndMissingCandidateKeepsLastGood()
    {
        using var runtime = CreateRuntime();
        var mixer = new AudioMixerAsset { mixerTypeId = "tests.audio.mixer" };

        Assert.True(runtime.ApplyMixer(mixer));
        Assert.True(runtime.SetBusVolume(new AudioBusId("tests.audio.bus.music"), 0.5f));

        mixer.mixerTypeId = "tests.audio.missing";
        Assert.False(runtime.ApplyMixer(mixer));
        Assert.True(runtime.SetBusMuted(new AudioBusId("tests.audio.bus.music"), true));
    }

    [Fact]
    public void DeviceReplacementCompletesOldHandlesAndUsesANewGeneration()
    {
        AudioClipAsset clip = CreateClip(frameCount: 480000);
        using var runtime = CreateRuntime();
        AudioVoiceHandle oldVoice = runtime.Play(clip);
        runtime.Update(0f);

        runtime.ReplaceDevice(new MutedAudioDevice());
        AudioVoiceHandle newVoice = runtime.Play(clip);

        Assert.NotEqual(oldVoice.deviceGeneration, newVoice.deviceGeneration);
        Assert.True(runtime.TryGetVoiceState(oldVoice, out AudioPlaybackState state));
        Assert.Equal(AudioPlaybackState.Completed, state);
        Assert.False(runtime.Stop(oldVoice));
    }

    [Fact]
    public void RecoveryCandidatePreservesTheLastGoodMixerGraph()
    {
        using var runtime = CreateRuntime();
        var mixer = new AudioMixerAsset { mixerTypeId = "tests.audio.mixer" };
        Assert.True(runtime.ApplyMixer(mixer));
        AudioBusId music = new("tests.audio.bus.music");
        Assert.True(runtime.SetBusVolume(music, 0.25f));
        Assert.True(runtime.SetBusPaused(music, true));
        var replacement = new ReadyAudioDevice();

        Assert.True(runtime.TryRecoverDevice(() => replacement));

        Assert.Equal(AudioDeviceState.Ready, runtime.deviceState);
        Assert.Equal(0.25f, replacement.GetBusVolume(music));
        Assert.True(replacement.GetBusPaused(music));
    }

    private AudioRuntimeLayer CreateRuntime(
        int maxVoices = 128,
        long decodedCacheBudgetBytes = 128L * 1024 * 1024,
        long automaticStreamingThresholdBytes = 1024)
        => new(
            m_types,
            new MutedAudioDevice(),
            m_artifacts,
            m_events,
            options: new AudioRuntimeOptions
            {
                maxVoices = maxVoices,
                decodedCacheBudgetBytes = decodedCacheBudgetBytes,
                automaticStreamingThresholdBytes = automaticStreamingThresholdBytes
            });

    private AudioClipAsset CreateClip(long frameCount)
    {
        var clip = new AudioClipAsset();
        byte[] payload = AudioClipMetadataCodec.Encode(new AudioClipMetadata(
            AudioCodecId.wav,
            2,
            48000,
            frameCount,
            256));
        AssetRuntimeHost.Initialize(
            clip,
            AssetPath.Project($"Audio/{clip.identity.persistentId:N}.wav"),
            "TEST",
            payload,
            isMissing: false,
            version: 1);
        m_artifacts.Add(clip.identity.persistentId, Path.Combine(m_root, $"{clip.identity.persistentId:N}.wav"));
        return clip;
    }

    [AudioMixerExtension("tests.audio.mixer")]
    private sealed class TestMixerExtension : AudioMixerExtension
    {
        public override void Build(AudioMixerBuilder builder, SerializedAudioExtensionState state)
        {
            builder.AddBus(new AudioBusId("tests.audio.bus.music"), AudioBusId.master);
            builder.AddProcessor(
                new AudioBusId("tests.audio.bus.music"),
                new AudioProcessorConfiguration(AudioProcessorId.lowPass));
        }
    }

    private sealed class FakeArtifactLookup : IAssetArtifactLookup
    {
        private readonly Dictionary<Guid, AssetArtifactInfo> m_artifacts = [];

        internal void Add(Guid id, string path)
        {
            File.WriteAllBytes(path, new byte[256]);
            m_artifacts[id] = new AssetArtifactInfo(
                new AssetArtifactKey("AABB"),
                "audio-data",
                path,
                "TEST",
                256);
        }

        public bool TryGetArtifact(Guid persistentId, string outputName, out AssetArtifactInfo? artifact)
        {
            if (outputName == "audio-data" && m_artifacts.TryGetValue(persistentId, out AssetArtifactInfo? found))
            {
                artifact = found;
                return true;
            }
            artifact = null;
            return false;
        }
    }

    private sealed class ReadyAudioDevice : IAudioDevice
    {
        private readonly Dictionary<AudioBusHandle, AudioBusId> m_busIds = [];
        private readonly Dictionary<AudioBusId, bool> m_busPaused = [];
        private readonly Dictionary<AudioBusId, float> m_busVolumes = [];
        private readonly IAudioDevice m_inner = new MutedAudioDevice();

        public AudioCapabilities capabilities => m_inner.capabilities;

        public uint generation => m_inner.generation;

        public AudioDeviceState state => AudioDeviceState.Ready;

        public double dspTime => m_inner.dspTime;

        public AudioStatistics statistics => m_inner.statistics;

        public AudioClipHandle CreateClip(AudioClipDescriptor descriptor) => m_inner.CreateClip(descriptor);

        public bool DestroyClip(AudioClipHandle clip) => m_inner.DestroyClip(clip);

        public AudioVoiceHandle Play(
            AudioClipHandle clip,
            AudioBusHandle bus,
            AudioPlayOptions options,
            double? scheduledDspTime = null)
            => m_inner.Play(clip, bus, options, scheduledDspTime);

        public bool Stop(AudioVoiceHandle voice) => m_inner.Stop(voice);

        public bool Pause(AudioVoiceHandle voice) => m_inner.Pause(voice);

        public bool Resume(AudioVoiceHandle voice) => m_inner.Resume(voice);

        public bool Seek(AudioVoiceHandle voice, TimeSpan position) => m_inner.Seek(voice, position);

        public bool SetVoiceParameters(AudioVoiceHandle voice, AudioVoiceParameters parameters)
            => m_inner.SetVoiceParameters(voice, parameters);

        public bool TryGetVoiceState(AudioVoiceHandle voice, out AudioPlaybackState playbackState)
            => m_inner.TryGetVoiceState(voice, out playbackState);

        public AudioBusHandle CreateBus(AudioBusId id, AudioBusHandle parent = default)
        {
            AudioBusHandle handle = m_inner.CreateBus(id, parent);
            if (handle.isValid)
                m_busIds.Add(handle, id);
            return handle;
        }

        public bool DestroyBus(AudioBusHandle bus) => m_inner.DestroyBus(bus);

        public bool SetBusVolume(AudioBusHandle bus, float volume)
        {
            if (!m_inner.SetBusVolume(bus, volume))
                return false;
            m_busVolumes[m_busIds[bus]] = volume;
            return true;
        }

        public bool SetBusMuted(AudioBusHandle bus, bool muted) => m_inner.SetBusMuted(bus, muted);

        public bool SetBusPaused(AudioBusHandle bus, bool paused)
        {
            if (!m_inner.SetBusPaused(bus, paused))
                return false;
            m_busPaused[m_busIds[bus]] = paused;
            return true;
        }

        public bool AddBusProcessor(AudioBusHandle bus, AudioProcessorConfiguration processor)
            => m_inner.AddBusProcessor(bus, processor);

        public AudioListenerHandle CreateListener(AudioListenerState state) => m_inner.CreateListener(state);

        public bool SetListener(AudioListenerHandle listener, AudioListenerState state)
            => m_inner.SetListener(listener, state);

        public bool DestroyListener(AudioListenerHandle listener) => m_inner.DestroyListener(listener);

        public void Update(float deltaTime) => m_inner.Update(deltaTime);

        public bool TryDequeueCompletion(out AudioDeviceCompletion completion)
            => m_inner.TryDequeueCompletion(out completion);

        public void Dispose() => m_inner.Dispose();

        internal float GetBusVolume(AudioBusId id) => m_busVolumes[id];

        internal bool GetBusPaused(AudioBusId id) => m_busPaused[id];
    }
}
