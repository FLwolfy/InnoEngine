using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inno.Audio;
using Xunit;

namespace Inno.Audio.Tests;

public sealed class AudioContractTests
{
    [Fact]
    public void ExecutionContext_ForwardsFacadeAndEnforcesLifoDisposal()
    {
        var outer = new FakeAudioService(1);
        var inner = new FakeAudioService(2);
        IDisposable outerScope = AudioExecutionContext.EnterScope(outer);
        IDisposable innerScope = AudioExecutionContext.EnterScope(inner);

        Assert.Same(inner.capabilities, Audio.capabilities);
        Assert.Throws<InvalidOperationException>(outerScope.Dispose);
        innerScope.Dispose();
        Assert.Same(outer.capabilities, Audio.capabilities);
        outerScope.Dispose();
        Assert.Throws<InvalidOperationException>(() => _ = Audio.dspTime);
    }

    [Fact]
    public void MixerBuilder_ProducesParentBeforeChildGraphWithMasterBus()
    {
        var builder = new AudioMixerBuilder();
        AudioBusId music = new("game.audio.bus.music");
        AudioBusId ambience = new("game.audio.bus.ambience");
        builder.AddBus(ambience, music);
        builder.AddBus(music, AudioBusId.master);
        builder.AddProcessor(
            music,
            new AudioProcessorConfiguration(
                AudioProcessorId.lowPass,
                [new AudioProcessorParameter(new AudioParameterId("frequency"), 12000f)]));

        AudioMixer mixer = builder.Build();

        Assert.Equal([AudioBusId.master, music, ambience],
            System.Linq.Enumerable.Select(mixer.buses, static bus => bus.id));
        Assert.Single(mixer.buses[1].processors);
    }

    [Fact]
    public void MixerBuilder_RejectsMissingParentsAndCycles()
    {
        var missingParent = new AudioMixerBuilder();
        missingParent.AddBus(new AudioBusId("game.audio.bus.music"), new AudioBusId("missing"));
        Assert.Throws<InvalidOperationException>(missingParent.Build);

        var cycle = new AudioMixerBuilder();
        AudioBusId first = new("game.audio.bus.first");
        AudioBusId second = new("game.audio.bus.second");
        cycle.AddBus(first, second);
        cycle.AddBus(second, first);
        Assert.Throws<InvalidOperationException>(cycle.Build);
    }

    [Fact]
    public void MetadataCodec_RoundTripsCompactRuntimeHeader()
    {
        var expected = new AudioClipMetadata(AudioCodecId.flac, 2, 48000, 96000, 123456);

        byte[] payload = AudioClipMetadataCodec.Encode(expected);
        AudioClipMetadata actual = AudioClipMetadataCodec.Decode(payload);

        Assert.Equal(expected, actual);
        Assert.Equal(TimeSpan.FromSeconds(2), actual.duration);
        Assert.Throws<InvalidOperationException>(() => AudioClipMetadataCodec.Decode(payload.AsSpan(1)));
    }

    [Fact]
    public void DeviceHandleGeneration_RejectsStaleHandles()
    {
        var first = new FakeAudioService(41);
        var replacement = new FakeAudioService(42);
        AudioVoiceHandle voice = first.CreateTestVoice();

        Assert.True(first.Owns(voice));
        Assert.False(replacement.Owns(voice));
    }

    private sealed class FakeAudioService(uint generation) : AudioDevice, IAudioService
    {
        public AudioCapabilities capabilities { get; } = new(true, true, true, 4, 48000);

        public AudioDeviceState deviceState => AudioDeviceState.Ready;

        public double dspTime => generation;

        public AudioStatistics statistics => default;

        public AudioVoiceHandle CreateTestVoice() => CreateVoiceHandle(1, generation);

        public bool Owns(AudioVoiceHandle voice)
        {
            DeviceHandleIdentity identity = GetHandleIdentity(voice);
            return identity.value != 0 && identity.generation == generation;
        }

        public AudioVoiceHandle Play(AudioClipAsset clip)
        {
            ArgumentNullException.ThrowIfNull(clip);
            return CreateTestVoice();
        }

        public AudioVoiceHandle Play(AudioClipAsset clip, AudioPlayOptions options)
            => Play(clip);

        public AudioVoiceHandle PlayScheduled(
            AudioClipAsset clip,
            double scheduledDspTime,
            AudioPlayOptions options)
            => Play(clip);

        public bool Stop(AudioVoiceHandle voice) => Owns(voice);

        public bool Pause(AudioVoiceHandle voice) => Owns(voice);

        public bool Resume(AudioVoiceHandle voice) => Owns(voice);

        public bool Seek(AudioVoiceHandle voice, TimeSpan position) => Owns(voice) && position >= TimeSpan.Zero;

        public bool SetVoiceParameters(AudioVoiceHandle voice, AudioVoiceParameters parameters) => Owns(voice);

        public bool TryGetVoiceState(AudioVoiceHandle voice, out AudioPlaybackState playbackState)
        {
            playbackState = Owns(voice) ? AudioPlaybackState.Playing : AudioPlaybackState.Invalid;
            return playbackState != AudioPlaybackState.Invalid;
        }

        public bool SetBusVolume(AudioBusId bus, float volume) => bus.isValid && volume >= 0f;

        public bool SetBusMuted(AudioBusId bus, bool muted) => bus.isValid;

        public bool SetBusPaused(AudioBusId bus, bool paused) => bus.isValid;

        public ValueTask PreloadAsync(
            AudioClipAsset clip,
            AudioClipLoadMode loadMode = AudioClipLoadMode.Automatic,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(clip);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public void ReleasePreload(AudioClipAsset clip) => ArgumentNullException.ThrowIfNull(clip);
    }
}
