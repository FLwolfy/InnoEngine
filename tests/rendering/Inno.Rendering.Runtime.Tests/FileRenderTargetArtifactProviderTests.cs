using System;
using System.IO;

using Inno.Core.Identity;
using Inno.Rendering;
using Xunit;

namespace Inno.Rendering.Runtime.Tests;

public sealed class FileRenderTargetArtifactProviderTests : IDisposable
{
    private readonly string m_root = Path.Combine(
        Path.GetTempPath(),
        "InnoRenderTargetArtifactProviderTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void TextureTargetArtifactLoadsWithoutAnyAuthoringSourceMount()
    {
        Directory.CreateDirectory(m_root);
        var identities = new IdentityAllocator();
        var texture = new TextureAsset(1, 1, TextureColorSpace.Srgb, "png");
        Guid persistentId = Guid.NewGuid();
        identities.InitializePersistentIdentity(texture, persistentId);
        string artifactPath = Path.Combine(
            m_root,
            RenderTargetArtifactPath.GetTexturePath(persistentId));
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        byte[] expected = [0xAB, 0x4B, 0x54, 0x58];
        File.WriteAllBytes(artifactPath, expected);
        var provider = new FileRenderTargetArtifactProvider(m_root);

        RenderTargetArtifactStatus status = provider.GetTextureArtifact(
            texture,
            out ReadOnlyMemory<byte> artifact);

        Assert.Equal(RenderTargetArtifactStatus.Ready, status);
        Assert.Equal(expected, artifact.ToArray());
        Assert.False(Directory.Exists(Path.Combine(m_root, "Sources")));
    }

    [Fact]
    public void MissingDeployedTextureArtifactIsReportedAsUnavailable()
    {
        Directory.CreateDirectory(m_root);
        var identities = new IdentityAllocator();
        var texture = new TextureAsset(1, 1, TextureColorSpace.Linear, "png");
        identities.InitializePersistentIdentity(texture, Guid.NewGuid());
        var provider = new FileRenderTargetArtifactProvider(m_root);

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
}
