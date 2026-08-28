using System;
using System.IO;
using Xunit;

namespace Inno.Rendering.Assets.Tests;

public sealed class TextureTargetCompilerTests : IDisposable
{
    private static readonly byte[] C_KTX_IDENTIFIER =
    [
        0xAB, 0x4B, 0x54, 0x58, 0x20, 0x31,
        0x31, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A
    ];

    private readonly string m_root;

    public TextureTargetCompilerTests()
    {
        m_root = Path.Combine(
            Path.GetTempPath(),
            "InnoRenderingTextureCompilerTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_root);
    }

    [Fact]
    public void CompileKtx_ProducesValidatedPortableContainer()
    {
        string sourcePath = Path.Combine(m_root, "checker.tga");
        File.WriteAllBytes(sourcePath, CreateTga());

        byte[] artifact = new TextureTargetCompiler().CompileKtx(sourcePath, TextureColorSpace.Srgb);

        Assert.True(artifact.Length > C_KTX_IDENTIFIER.Length);
        Assert.Equal(C_KTX_IDENTIFIER, artifact[..C_KTX_IDENTIFIER.Length]);
    }

    public void Dispose()
    {
        if (Directory.Exists(m_root))
        {
            Directory.Delete(m_root, recursive: true);
        }
    }

    private static byte[] CreateTga()
    {
        byte[] bytes = new byte[18 + (2 * 2 * 4)];
        bytes[2] = 2;
        bytes[12] = 2;
        bytes[14] = 2;
        bytes[16] = 32;
        bytes[17] = 0x28;

        ReadOnlySpan<byte> pixels =
        [
            0x00, 0x00, 0xFF, 0xFF,
            0x00, 0xFF, 0x00, 0xFF,
            0xFF, 0x00, 0x00, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF
        ];
        pixels.CopyTo(bytes.AsSpan(18));
        return bytes;
    }
}
