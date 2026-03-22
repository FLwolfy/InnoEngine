using System;

namespace Inno.Platform.Graphics;

public enum PixelFormat
{
    R8_G8_B8_A8_UNorm,
    D32_Float_S8_UInt
}

[Flags]
public enum TextureUsage : byte
{
    // NOTE: This is copied from Veldrid.TextureUsage.
    //       Values must match exactly.
    Sampled = 1 << 0,
    Storage = 1 << 1,
    RenderTarget = 1 << 2,
    DepthStencil = 1 << 3,
    Cubemap = 1 << 4,
    Staging = 1 << 5,
    GenerateMipmaps = 1 << 6,
}

public enum TextureDimension
{
    Texture2D,
    Texture3D
}

public struct TextureDescription()
{
    public int width;
    public int height;
    public int mipLevelCount = 1;
    
    public PixelFormat format;
    public TextureUsage usage;
    public TextureDimension dimension;
}

public interface ITexture : IDisposable
{
    int width { get; }
    int height { get; }

    void Set(ref byte[] data, int mipLevel = 0);
}