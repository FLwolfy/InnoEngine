using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public static class BgfxFormatConverter
{
    public static bgfx.TextureFormat ToBgfxTextureFormat(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.R8Unorm => bgfx.TextureFormat.R8,
            PixelFormat.R8G8B8A8Unorm => bgfx.TextureFormat.RGBA8,
            PixelFormat.B8G8R8A8Unorm => bgfx.TextureFormat.BGRA8,
            PixelFormat.R16G16B16A16Float => bgfx.TextureFormat.RGBA16F,
            PixelFormat.R32G32B32A32Float => bgfx.TextureFormat.RGBA32F,
            PixelFormat.D24UnormS8Uint => bgfx.TextureFormat.D24S8,
            PixelFormat.D32Float => bgfx.TextureFormat.D32F,
            _ => bgfx.TextureFormat.BGRA8
        };
    }
}

