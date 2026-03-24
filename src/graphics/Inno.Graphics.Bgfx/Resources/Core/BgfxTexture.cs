using Inno.Graphics;
using Inno.Native.Bgfx;
using System.Runtime.InteropServices;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxTexture : DisposableGraphicsResource, IGraphicsTexture
{
    private readonly TextureDescription m_description;
    private readonly bool m_ownsHandle;
    private byte[] m_data = [];
    private bgfx.TextureHandle m_handle = new() { idx = ushort.MaxValue };

    public BgfxTexture(TextureDescription description)
    {
        m_description = description;
        m_ownsHandle = true;
        width = description.width;
        height = description.height;
        format = description.format;
        CreateNativeTexture();
    }

    internal BgfxTexture(int width, int height, PixelFormat format, bgfx.TextureHandle handle, bool ownsHandle)
    {
        m_description = new TextureDescription
        {
            width = width,
            height = height,
            format = format
        };
        m_ownsHandle = ownsHandle;
        this.width = width;
        this.height = height;
        this.format = format;
        m_handle = handle;
    }

    public int width { get; }

    public int height { get; }

    public PixelFormat format { get; }

    public void SetData<T>(ReadOnlySpan<T> data, int mipLevel = 0) where T : unmanaged
    {
        m_data = MemoryMarshal.AsBytes(data).ToArray();
        if (!m_handle.Valid || m_description.dimension != TextureDimension.Texture2D)
        {
            return;
        }

        unsafe
        {
            fixed (byte* ptr = m_data)
            {
                var mem = bgfx.copy(ptr, (uint)m_data.Length);
                var bytesPerPixel = EstimateBytesPerPixel(format);
                var pitch = (ushort)Math.Max(0, width * bytesPerPixel);
                bgfx.update_texture_2d(m_handle, 0, (byte)mipLevel, 0, 0, (ushort)width, (ushort)height, mem, pitch);
            }
        }
    }

    internal bgfx.TextureHandle handle => m_handle;

    private unsafe void CreateNativeTexture()
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var bgfxFormat = BgfxFormatConverter.ToBgfxTextureFormat(format);
        var textureFlags = 0UL;
        m_handle = m_description.dimension switch
        {
            TextureDimension.TextureCube => bgfx.create_texture_cube((ushort)Math.Min(width, height), false, 1, bgfxFormat, textureFlags, null, 0),
            TextureDimension.Texture3D => bgfx.create_texture_3d((ushort)width, (ushort)height, (ushort)Math.Max(1, m_description.depthOrLayers), false, bgfxFormat, textureFlags, null, 0),
            _ => bgfx.create_texture_2d((ushort)width, (ushort)height, m_description.mipLevels > 1, (ushort)Math.Max(1, m_description.depthOrLayers), bgfxFormat, textureFlags, null, 0)
        };
    }

    private static int EstimateBytesPerPixel(PixelFormat pixelFormat)
    {
        return pixelFormat switch
        {
            PixelFormat.R8Unorm => 1,
            PixelFormat.R8G8B8A8Unorm => 4,
            PixelFormat.B8G8R8A8Unorm => 4,
            PixelFormat.R16G16B16A16Float => 8,
            PixelFormat.R32G32B32A32Float => 16,
            PixelFormat.D24UnormS8Uint => 4,
            PixelFormat.D32Float => 4,
            _ => 4
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (m_ownsHandle && m_handle.Valid)
        {
            bgfx.destroy_texture(m_handle);
            m_handle = default;
        }
    }
}

