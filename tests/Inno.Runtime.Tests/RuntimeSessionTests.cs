using System;
using System.IO;

using Inno.Core.Identity;
using Inno.Rendering;
using Inno.Runtime;
using Inno.Scene;

using Xunit;

namespace Inno.Runtime.Tests;

public sealed class RuntimeSessionTests : IDisposable
{
    private readonly string m_root = Path.Combine(
        Path.GetTempPath(),
        "InnoRuntimeSessionTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void MultipleHostsAndSessionsKeepSceneAndTimeStateIsolated()
    {
        using EngineHost firstHost = CreateHost("First");
        using EngineHost secondHost = CreateHost("Second");
        using RuntimeSession first = firstHost.CreateSession(CreateOptions("first", RuntimeSessionKind.Play));
        using RuntimeSession second = secondHost.CreateSession(CreateOptions("second", RuntimeSessionKind.Play));

        using (first.EnterExecutionScope())
            _ = SceneManager.LoadNewScene("First Scene");
        using (second.EnterExecutionScope())
            _ = SceneManager.LoadNewScene("Second Scene");

        first.Tick(1f, 0.02f);
        second.Tick(7f, 0.04f);

        using (first.EnterExecutionScope())
        {
            Assert.Equal("First Scene", Assert.Single(SceneManager.loadedScenes).name);
            Assert.Equal(1f, Time.time);
            Assert.Equal(0.02f, Time.deltaTime);
        }
        using (second.EnterExecutionScope())
        {
            Assert.Equal("Second Scene", Assert.Single(SceneManager.loadedScenes).name);
            Assert.Equal(7f, Time.time);
            Assert.Equal(0.04f, Time.deltaTime);
        }
    }

    [Fact]
    public void ScriptFacadesRejectCallsOutsideAnActiveSession()
    {
        Assert.Throws<InvalidOperationException>(() => _ = SceneManager.loadedScenes);
        Assert.Throws<InvalidOperationException>(() => _ = Time.deltaTime);
    }

    [Fact]
    public void DisposedHostRejectsNewSessions()
    {
        EngineHost host = CreateHost("Disposed");
        host.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => host.CreateSession(CreateOptions("disposed", RuntimeSessionKind.Edit)));
    }

    [Fact]
    public void TextureTargetArtifactLoadsWithoutAnyAuthoringSourceMount()
    {
        string contentRoot = Path.Combine(m_root, "ArtifactOnlyContent");
        Directory.CreateDirectory(contentRoot);
        var identities = new IdentityAllocator();
        var texture = new TextureAsset(1, 1, TextureColorSpace.Srgb, "png");
        Guid persistentId = Guid.NewGuid();
        identities.InitializePersistentIdentity(texture, persistentId);
        string artifactPath = Path.Combine(
            contentRoot,
            RenderTargetArtifactPath.GetTexturePath(persistentId));
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        byte[] expected = [0xAB, 0x4B, 0x54, 0x58];
        File.WriteAllBytes(artifactPath, expected);
        var provider = new FileRenderTargetArtifactProvider(contentRoot);

        RenderTargetArtifactStatus status = provider.GetTextureArtifact(
            texture,
            out ReadOnlyMemory<byte> artifact);

        Assert.Equal(RenderTargetArtifactStatus.Ready, status);
        Assert.Equal(expected, artifact.ToArray());
        Assert.False(Directory.Exists(Path.Combine(contentRoot, "Sources")));
    }

    [Fact]
    public void MissingDeployedTextureArtifactIsReportedAsUnavailable()
    {
        string contentRoot = Path.Combine(m_root, "MissingArtifactContent");
        Directory.CreateDirectory(contentRoot);
        var identities = new IdentityAllocator();
        var texture = new TextureAsset(1, 1, TextureColorSpace.Linear, "png");
        identities.InitializePersistentIdentity(texture, Guid.NewGuid());
        var provider = new FileRenderTargetArtifactProvider(contentRoot);

        RenderTargetArtifactStatus status = provider.GetTextureArtifact(
            texture,
            out ReadOnlyMemory<byte> artifact);

        Assert.Equal(RenderTargetArtifactStatus.Unavailable, status);
        Assert.True(artifact.IsEmpty);
    }

    public void Dispose()
    {
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    private EngineHost CreateHost(string name)
        => new EngineHostBuilder()
            .UseMetadataCache(Path.Combine(m_root, name, "Metadata"))
            .Build();

    private RuntimeSessionOptions CreateOptions(string applicationId, RuntimeSessionKind kind)
        => new()
        {
            kind = kind,
            applicationId = applicationId,
            persistentDataDirectory = Path.Combine(m_root, "Persistent", applicationId),
            fixedDeltaTime = 0.02f,
            jobExecutionMode = RuntimeJobExecutionMode.SingleThread
        };
}
