using System;
using System.Collections.Generic;
using System.IO;
using Inno.Assets;
using Inno.Audio;
using Inno.Audio.Runtime;
using Inno.Runtime;
using Xunit;

namespace Inno.Editor.Audio.Tests;

public sealed class EditorAudioHostTests : IDisposable
{
    private readonly FakeArtifactLookup m_artifacts = new();
    private readonly EngineHost m_engine;
    private readonly string m_root;

    public EditorAudioHostTests()
    {
        m_root = Path.Combine(Path.GetTempPath(), "InnoEditorAudioTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_root);
        m_engine = new EngineHostBuilder()
            .UseMetadataCache(Path.Combine(m_root, "Assemblies"))
            .Build();
    }

    public void Dispose()
    {
        m_engine.Dispose();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    [Fact]
    public void EditAndPlaySessionsOwnIndependentExecutionScopesAndLifetimes()
    {
        using var host = new EditorAudioHost(
            m_engine.types,
            m_artifacts,
            m_engine.logs,
            deviceFactory: static () => new MutedAudioDevice());
        using RuntimeSession edit = m_engine.CreateSession(CreateOptions(RuntimeSessionKind.Edit, "edit"));
        using RuntimeSession play = m_engine.CreateSession(CreateOptions(RuntimeSessionKind.Play, "play"));
        using IDisposable editLease = host.BeginSession(edit);
        using (host.EnterExecutionScope(edit))
            Assert.Equal(AudioDeviceState.Muted, Inno.Audio.Audio.deviceState);

        using IDisposable playLease = host.BeginSession(play);
        using (host.EnterExecutionScope(play))
            Assert.Equal(AudioDeviceState.Muted, Inno.Audio.Audio.deviceState);
        host.Update(play, 0.016f);
        playLease.Dispose();

        using (host.EnterExecutionScope(edit))
            Assert.Equal(AudioDeviceState.Muted, Inno.Audio.Audio.deviceState);
        host.Update(edit, 0.016f);
    }

    [Fact]
    public void PreviewUsesTheRealAudioServiceAndRejectsPlaySessionPreview()
    {
        using var host = new EditorAudioHost(
            m_engine.types,
            m_artifacts,
            m_engine.logs,
            deviceFactory: static () => new MutedAudioDevice());
        using RuntimeSession edit = m_engine.CreateSession(CreateOptions(RuntimeSessionKind.Edit, "preview-edit"));
        using RuntimeSession play = m_engine.CreateSession(CreateOptions(RuntimeSessionKind.Play, "preview-play"));
        using IDisposable editLease = host.BeginSession(edit);
        using IDisposable playLease = host.BeginSession(play);
        AudioClipAsset clip = CreateClip();

        AudioVoiceHandle voice = host.PlayPreview(edit, clip);
        Assert.True(voice.isValid);
        host.Update(edit, 0f);
        Assert.True(host.StopPreview(edit, voice));
        Assert.Throws<ArgumentException>(() => host.PlayPreview(play, clip));
    }

    private RuntimeSessionOptions CreateOptions(RuntimeSessionKind kind, string name)
        => new()
        {
            kind = kind,
            applicationId = name,
            persistentDataDirectory = Path.Combine(m_root, "Persistent", name),
            jobExecutionMode = RuntimeJobExecutionMode.SingleThread
        };

    private AudioClipAsset CreateClip()
    {
        var clip = new AudioClipAsset();
        byte[] payload = AudioClipMetadataCodec.Encode(new AudioClipMetadata(
            AudioCodecId.wav,
            1,
            48000,
            480000,
            128));
        AssetRuntimeHost.Initialize(
            clip,
            AssetPath.Project("Audio/Preview.wav"),
            "TEST",
            payload,
            isMissing: false,
            version: 1);
        string path = Path.Combine(m_root, "preview.wav");
        File.WriteAllBytes(path, new byte[128]);
        m_artifacts.Add(clip.identity.persistentId, path);
        return clip;
    }

    private sealed class FakeArtifactLookup : IAssetArtifactLookup
    {
        private readonly Dictionary<Guid, AssetArtifactInfo> m_artifacts = [];

        internal void Add(Guid id, string path)
            => m_artifacts.Add(
                id,
                new AssetArtifactInfo(new AssetArtifactKey("AABB"), "audio-data", path, "TEST", 128));

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
}
