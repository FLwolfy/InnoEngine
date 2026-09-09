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
        RenderTextureArtifactReference reference = texture.GetTextureArtifactReference();
        string artifactPath = Path.Combine(
            m_root,
            RenderTargetArtifactPath.GetTexturePath(reference));
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        byte[] expected = [0xAB, 0x4B, 0x54, 0x58];
        File.WriteAllBytes(artifactPath, expected);
        var provider = new FileRenderTargetArtifactProvider(m_root);

        RenderTargetArtifactStatus status = provider.GetTextureArtifact(
            reference,
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
        RenderTextureArtifactReference reference = texture.GetTextureArtifactReference();
        var provider = new FileRenderTargetArtifactProvider(m_root);

        RenderTargetArtifactStatus status = provider.GetTextureArtifact(
            reference,
            out ReadOnlyMemory<byte> artifact);

        Assert.Equal(RenderTargetArtifactStatus.Unavailable, status);
        Assert.True(artifact.IsEmpty);
    }

    [Fact]
    public void IndependentTextureSlotsUseStableDistinctDeploymentPaths()
    {
        Directory.CreateDirectory(m_root);
        Guid assetId = Guid.NewGuid();
        var firstSlot = new RenderTextureArtifactSlot("page-a", "texture-page-a", TextureColorSpace.Srgb);
        var secondSlot = new RenderTextureArtifactSlot("page-b", "texture-page-b", TextureColorSpace.Linear);
        var first = new RenderTextureArtifactReference(assetId, 4, firstSlot);
        var second = new RenderTextureArtifactReference(assetId, 4, secondSlot);
        string firstRelativePath = RenderTargetArtifactPath.GetTexturePath(first);
        string secondRelativePath = RenderTargetArtifactPath.GetTexturePath(second);

        Assert.NotEqual(firstRelativePath, secondRelativePath);
        Assert.Equal(firstRelativePath, RenderTargetArtifactPath.GetTexturePath(first));
        Assert.StartsWith($"TargetArtifacts/Textures/{assetId:D}/", firstRelativePath, StringComparison.Ordinal);

        string firstPath = Path.Combine(m_root, firstRelativePath);
        string secondPath = Path.Combine(m_root, secondRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);
        File.WriteAllBytes(firstPath, [0x01]);
        File.WriteAllBytes(secondPath, [0x02]);
        var provider = new FileRenderTargetArtifactProvider(m_root);

        Assert.Equal(RenderTargetArtifactStatus.Ready, provider.GetTextureArtifact(first, out ReadOnlyMemory<byte> firstBytes));
        Assert.Equal(RenderTargetArtifactStatus.Ready, provider.GetTextureArtifact(second, out ReadOnlyMemory<byte> secondBytes));
        Assert.Equal([0x01], firstBytes.ToArray());
        Assert.Equal([0x02], secondBytes.ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }
}
