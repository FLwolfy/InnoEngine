using System;
using System.Collections.Generic;
using System.IO;
using Inno.Assets;
using Inno.Audio.Runtime;
using Inno.Core.Events;
using Inno.Core.Identity;
using Inno.Core.Mathematics;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Scene;
using Xunit;

namespace Inno.Audio.Scene.Tests;

public sealed class SceneAudioIntegrationTests : IDisposable
{
    private readonly FakeArtifactLookup m_artifacts = new();
    private readonly EventDispatcher m_events = new();
    private readonly IdentityAllocator m_identities = new();
    private readonly ModuleHost m_modules;
    private readonly string m_root;
    private readonly TypeCatalog m_types;
    private readonly SceneWorld m_world;

    public SceneAudioIntegrationTests()
    {
        m_root = Path.Combine(Path.GetTempPath(), "InnoAudioSceneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_root);
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_root, "Assemblies")
        });
        _ = typeof(AudioSource);
        m_types = new TypeCatalog(m_modules);
        m_types.Rebuild();
        using IDisposable identityScope = m_identities.EnterScope();
        m_world = new SceneWorld(m_identities, m_types);
    }

    public void Dispose()
    {
        using IDisposable identityScope = m_identities.EnterScope();
        m_world.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    [Fact]
    public void SourceAndListenerSynchronizeIncrementallyAndStopWhenDisabled()
    {
        using IDisposable identityScope = m_identities.EnterScope();
        using IDisposable worldScope = m_world.EnterScope();
        GameScene scene = m_world.LoadNewScene("Audio Scene");
        AudioClipAsset clip = CreateClip(frameCount: 480000);
        GameObject emitterObject = scene.CreateObject("Emitter");
        AudioSource source = emitterObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.spatialize = true;
        source.loop = true;
        emitterObject.transform.worldPosition = new Vector3(1f, 2f, 3f);
        GameObject listenerObject = scene.CreateObject("Listener");
        AudioListener listener = listenerObject.AddComponent<AudioListener>();
        listener.priority = 10;
        SceneAudioContent content = new(m_world);
        using var runtime = new AudioRuntimeLayer(
            m_types,
            new MutedAudioDevice(),
            m_artifacts,
            m_events,
            contentScopeProvider: content.Capture);

        m_world.Update(0.016f);
        runtime.Update(0.016f);

        Assert.True(source.isPlaybackRequested);
        Assert.Equal(1, runtime.statistics.activeVoices);
        emitterObject.transform.worldPosition = new Vector3(3f, 2f, 1f);
        runtime.Update(0.5f);
        Assert.Equal(1, runtime.statistics.activeVoices);

        source.enabled = false;
        listener.enabled = false;
        runtime.Update(0.016f);

        Assert.Equal(0, runtime.statistics.activeVoices);
    }

    [Fact]
    public void ExplicitReplayRevisionRestartsOneShotAndListenerTieIsDiagnosed()
    {
        using IDisposable identityScope = m_identities.EnterScope();
        using IDisposable worldScope = m_world.EnterScope();
        GameScene scene = m_world.LoadNewScene("Replay Scene");
        AudioClipAsset clip = CreateClip(frameCount: 480000);
        AudioSource source = scene.CreateObject("Source").AddComponent<AudioSource>();
        source.clip = clip;
        source.playOnAwake = false;
        source.Play();
        scene.CreateObject("Listener A").AddComponent<AudioListener>().priority = 5;
        scene.CreateObject("Listener B").AddComponent<AudioListener>().priority = 5;
        var diagnostics = new CollectingDiagnostics();
        SceneAudioContent content = new(m_world);
        using var runtime = new AudioRuntimeLayer(
            m_types,
            new MutedAudioDevice(),
            m_artifacts,
            m_events,
            diagnostics,
            contentScopeProvider: content.Capture);
        using EventHub hub = m_events.CreateHub();
        var completions = new List<AudioVoiceCompletedEvent>();
        hub.Listen<AudioVoiceCompletedEvent>(completions.Add);

        runtime.Update(0f);
        source.Play();
        runtime.Update(0f);
        m_events.Flush();

        Assert.Equal(1, runtime.statistics.activeVoices);
        Assert.Contains(completions, static completion => completion.reason == AudioCompletionReason.Stopped);
        Assert.Contains(diagnostics.items, static item => item.code == "AUDIO_LISTENER_PRIORITY_TIE");
    }

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

    private sealed class FakeArtifactLookup : IAssetArtifactLookup
    {
        private readonly Dictionary<Guid, AssetArtifactInfo> m_artifacts = [];

        internal void Add(Guid id, string path)
        {
            File.WriteAllBytes(path, new byte[256]);
            m_artifacts[id] = new AssetArtifactInfo(new AssetArtifactKey("AABB"), "audio-data", path, "TEST", 256);
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

    private sealed class CollectingDiagnostics : IAudioDiagnosticSink
    {
        internal List<AudioDiagnostic> items { get; } = [];

        public void Publish(AudioDiagnostic diagnostic) => items.Add(diagnostic);
    }
}
