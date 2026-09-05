using System;
using System.Collections.Generic;
using System.IO;
using Inno.Audio;
using Inno.Audio.MiniAudio;
using Inno.Core.Mathematics;
using Xunit;

namespace Inno.Audio.MiniAudio.Tests;

public sealed class MiniAudioDeviceTests
{
    [Fact]
    public void HeadlessDeviceAdvancesNativeGraphAndCompletesVoice()
    {
        string clipPath = CreateWaveFile(frameCount: 2400);
        try
        {
            using MiniAudioDevice device = new(new MiniAudioDeviceOptions
            {
                noDevice = true,
                channels = 2,
                sampleRate = 48000,
                listenerCount = 2
            });
            IAudioDevice backend = device;
            AudioBusHandle master = backend.CreateBus(AudioBusId.master);
            AudioClipHandle clip = backend.CreateClip(new AudioClipDescriptor(
                clipPath,
                AudioCodecId.wav,
                AudioClipLoadMode.Decode,
                1,
                48000,
                2400,
                new FileInfo(clipPath).Length));
            AudioListenerHandle listener = backend.CreateListener(new AudioListenerState(
                Vector3.ZERO,
                Vector3.FORWARD,
                Vector3.UP,
                Vector3.ZERO));
            AudioVoiceHandle voice = backend.Play(
                clip,
                master,
                new AudioPlayOptions(spatial: new AudioSpatialOptions(
                    new Vector3(2f, 0f, 0f),
                    Vector3.FORWARD,
                    Vector3.ZERO)),
                null);

            Assert.Equal(AudioDeviceState.Muted, backend.state);
            Assert.Equal(2, backend.capabilities.maxListeners);
            Assert.True(voice.isValid);
            Assert.True(backend.SetListener(listener, new AudioListenerState(
                new Vector3(1f, 0f, 0f),
                Vector3.FORWARD,
                Vector3.UP,
                Vector3.ZERO)));

            backend.Update(0.1f);

            Assert.True(backend.dspTime >= 0.05d);
            Assert.True(backend.TryDequeueCompletion(out AudioDeviceCompletion completion));
            Assert.Equal(voice, completion.voice);
            Assert.Equal(AudioCompletionReason.NaturalEnd, completion.reason);
            Assert.False(backend.TryGetVoiceState(voice, out AudioPlaybackState terminalState));
            Assert.Equal(AudioPlaybackState.Invalid, terminalState);
            Assert.True(backend.DestroyListener(listener));
            Assert.True(backend.DestroyClip(clip));
            Assert.True(backend.DestroyBus(master));
        }
        finally
        {
            File.Delete(clipPath);
        }
    }

    [Fact]
    public void StandardProcessorsAndScheduledControlsUseNativeNodes()
    {
        string clipPath = CreateWaveFile(frameCount: 4800);
        try
        {
            using MiniAudioDevice device = new(new MiniAudioDeviceOptions { noDevice = true });
            IAudioDevice backend = device;
            AudioBusHandle master = backend.CreateBus(AudioBusId.master);
            AudioBusHandle effects = backend.CreateBus(new AudioBusId("test.effects"), master);
            foreach (AudioProcessorId processorId in StandardProcessors())
                Assert.True(backend.AddBusProcessor(effects, CreateProcessor(processorId)));

            AudioClipHandle clip = backend.CreateClip(new AudioClipDescriptor(
                clipPath,
                AudioCodecId.wav,
                AudioClipLoadMode.Stream,
                1,
                48000,
                4800,
                new FileInfo(clipPath).Length));
            AudioVoiceHandle voice = backend.Play(
                clip,
                effects,
                new AudioPlayOptions(loop: true),
                backend.dspTime + 0.05d);

            Assert.True(backend.TryGetVoiceState(voice, out AudioPlaybackState scheduled));
            Assert.Equal(AudioPlaybackState.Scheduled, scheduled);
            backend.Update(0.06f);
            Assert.True(backend.TryGetVoiceState(voice, out AudioPlaybackState playing));
            Assert.Equal(AudioPlaybackState.Playing, playing);
            Assert.True(backend.Pause(voice));
            Assert.True(backend.Seek(voice, TimeSpan.FromMilliseconds(10)));
            Assert.True(backend.SetVoiceParameters(voice, new AudioVoiceParameters(0.5f, 1.25f, -0.25f)));
            Assert.True(backend.Resume(voice));
            Assert.True(backend.Stop(voice));
            Assert.True(backend.TryDequeueCompletion(out AudioDeviceCompletion completion));
            Assert.Equal(AudioCompletionReason.Stopped, completion.reason);
            Assert.True(backend.DestroyClip(clip));
            Assert.True(backend.DestroyBus(effects));
            Assert.True(backend.DestroyBus(master));
        }
        finally
        {
            File.Delete(clipPath);
        }
    }

    [Fact]
    public void MixerGenerationsCanOverlapUntilOldVoicesReleaseTheirGraph()
    {
        string clipPath = CreateWaveFile(frameCount: 48000);
        try
        {
            using MiniAudioDevice device = new(new MiniAudioDeviceOptions { noDevice = true });
            IAudioDevice backend = device;
            AudioBusHandle oldMaster = backend.CreateBus(AudioBusId.master);
            AudioBusHandle oldMusic = backend.CreateBus(new AudioBusId("test.music"), oldMaster);
            AudioClipHandle clip = backend.CreateClip(new AudioClipDescriptor(
                clipPath,
                AudioCodecId.wav,
                AudioClipLoadMode.Decode,
                1,
                48000,
                48000,
                new FileInfo(clipPath).Length));
            AudioVoiceHandle voice = backend.Play(
                clip,
                oldMusic,
                new AudioPlayOptions(loop: true),
                null);

            AudioBusHandle newMaster = backend.CreateBus(AudioBusId.master);
            AudioBusHandle newMusic = backend.CreateBus(new AudioBusId("test.music"), newMaster);

            Assert.True(newMaster.isValid);
            Assert.True(newMusic.isValid);
            Assert.False(backend.DestroyBus(oldMusic));
            Assert.True(backend.Stop(voice));
            Assert.True(backend.DestroyClip(clip));
            Assert.True(backend.DestroyBus(oldMusic));
            Assert.True(backend.DestroyBus(oldMaster));
            Assert.True(backend.DestroyBus(newMusic));
            Assert.True(backend.DestroyBus(newMaster));
        }
        finally
        {
            File.Delete(clipPath);
        }
    }

    private static IEnumerable<AudioProcessorId> StandardProcessors()
    {
        yield return AudioProcessorId.lowPass;
        yield return AudioProcessorId.highPass;
        yield return AudioProcessorId.bandPass;
        yield return AudioProcessorId.notch;
        yield return AudioProcessorId.peak;
        yield return AudioProcessorId.lowShelf;
        yield return AudioProcessorId.highShelf;
        yield return AudioProcessorId.delay;
    }

    private static AudioProcessorConfiguration CreateProcessor(AudioProcessorId id)
    {
        if (id == AudioProcessorId.delay)
        {
            return new AudioProcessorConfiguration(id,
            [
                new AudioProcessorParameter(AudioParameterId.delayMilliseconds, 5f),
                new AudioProcessorParameter(AudioParameterId.decay, 0.2f)
            ]);
        }
        return new AudioProcessorConfiguration(id,
        [
            new AudioProcessorParameter(AudioParameterId.frequency, 1000f),
            new AudioProcessorParameter(AudioParameterId.quality, 0.707f),
            new AudioProcessorParameter(AudioParameterId.gainDecibels, 2f),
            new AudioProcessorParameter(AudioParameterId.shelfSlope, 1f)
        ]);
    }

    private static string CreateWaveFile(int frameCount)
    {
        string path = Path.Combine(Path.GetTempPath(), $"inno-miniaudio-{Guid.NewGuid():N}.wav");
        const int sampleRate = 48000;
        const short channels = 1;
        const short bitsPerSample = 16;
        int dataLength = frameCount * channels * (bitsPerSample / 8);
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);
        for (int frame = 0; frame < frameCount; frame++)
        {
            double phase = 2d * Math.PI * 440d * frame / sampleRate;
            writer.Write((short)(Math.Sin(phase) * short.MaxValue * 0.1d));
        }
        return path;
    }
}
