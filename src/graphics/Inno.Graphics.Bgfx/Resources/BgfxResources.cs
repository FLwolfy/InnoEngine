using Inno.Graphics;
using Inno.Native.Bgfx;
using System.Runtime.InteropServices;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxBuffer : DisposableGraphicsResource, IGraphicsBuffer
{
    private byte[] m_data = [];
    private bool m_isDirty;
    private int m_lastSetElementSize;
    private bgfx.VertexBufferHandle m_vertexHandle = new() { idx = ushort.MaxValue };
    private bgfx.IndexBufferHandle m_indexHandle = new() { idx = ushort.MaxValue };

    public BgfxBuffer(BufferDescription description)
    {
        sizeInBytes = description.sizeInBytes;
        usage = description.usage;
    }

    public int sizeInBytes { get; }

    public GraphicsBufferUsage usage { get; }

    public void SetData<T>(ReadOnlySpan<T> data, int destinationOffsetInBytes = 0) where T : unmanaged
    {
        m_lastSetElementSize = Marshal.SizeOf<T>();
        var byteCount = data.Length * m_lastSetElementSize;
        if (destinationOffsetInBytes < 0 || destinationOffsetInBytes + byteCount > sizeInBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationOffsetInBytes));
        }

        if (m_data.Length != sizeInBytes)
        {
            m_data = new byte[sizeInBytes];
        }

        var dst = m_data.AsSpan(destinationOffsetInBytes, byteCount);
        MemoryMarshal.AsBytes(data).CopyTo(dst);
        m_isDirty = true;
    }

    internal unsafe bgfx.VertexBufferHandle EnsureVertexBuffer(BgfxInputLayout inputLayout)
    {
        if (usage != GraphicsBufferUsage.Vertex)
        {
            throw new InvalidOperationException("Buffer usage is not vertex.");
        }

        if (m_vertexHandle.Valid && !m_isDirty)
        {
            return m_vertexHandle;
        }

        if (m_vertexHandle.Valid)
        {
            bgfx.destroy_vertex_buffer(m_vertexHandle);
        }

        fixed (byte* data = m_data)
        {
            var mem = bgfx.copy(data, (uint)m_data.Length);
            var layout = inputLayout.nativeLayout;
            m_vertexHandle = bgfx.create_vertex_buffer(mem, &layout, 0);
        }

        m_isDirty = false;
        return m_vertexHandle;
    }

    internal unsafe bgfx.IndexBufferHandle EnsureIndexBuffer()
    {
        if (usage != GraphicsBufferUsage.Index)
        {
            throw new InvalidOperationException("Buffer usage is not index.");
        }

        if (m_indexHandle.Valid && !m_isDirty)
        {
            return m_indexHandle;
        }

        if (m_indexHandle.Valid)
        {
            bgfx.destroy_index_buffer(m_indexHandle);
        }

        fixed (byte* data = m_data)
        {
            var mem = bgfx.copy(data, (uint)m_data.Length);
            var flags = m_lastSetElementSize == sizeof(uint)
                ? (ushort)bgfx.BufferFlags.Index32
                : (ushort)0;
            m_indexHandle = bgfx.create_index_buffer(mem, flags);
        }

        m_isDirty = false;
        return m_indexHandle;
    }

    internal uint GetVertexCount(int stride)
    {
        if (stride <= 0 || m_data.Length < stride)
        {
            return 0;
        }

        return (uint)(m_data.Length / stride);
    }

    protected override void Dispose(bool disposing)
    {
        if (m_vertexHandle.Valid)
        {
            bgfx.destroy_vertex_buffer(m_vertexHandle);
            m_vertexHandle = default;
        }

        if (m_indexHandle.Valid)
        {
            bgfx.destroy_index_buffer(m_indexHandle);
            m_indexHandle = default;
        }
    }
}

public sealed class BgfxTexture : DisposableGraphicsResource, IGraphicsTexture
{
    private readonly TextureDescription m_description;
    private byte[] m_data = [];
    private bgfx.TextureHandle m_handle = new() { idx = ushort.MaxValue };

    public BgfxTexture(TextureDescription description)
    {
        m_description = description;
        width = description.width;
        height = description.height;
        format = description.format;
        CreateNativeTexture();
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
        if (m_handle.Valid)
        {
            bgfx.destroy_texture(m_handle);
            m_handle = default;
        }
    }
}

public sealed class BgfxSampler : DisposableGraphicsResource, IGraphicsSampler
{
}

public sealed class BgfxRenderTarget : DisposableGraphicsResource, IGraphicsRenderTarget
{
    private bgfx.FrameBufferHandle m_frameBufferHandle = new() { idx = ushort.MaxValue };

    public BgfxRenderTarget(GraphicsRenderTargetDescription description)
    {
        width = description.width;
        height = description.height;
    }

    public int width { get; }

    public int height { get; }

    internal bgfx.FrameBufferHandle frameBufferHandle => m_frameBufferHandle;

    protected override void Dispose(bool disposing)
    {
        if (m_frameBufferHandle.Valid)
        {
            bgfx.destroy_frame_buffer(m_frameBufferHandle);
            m_frameBufferHandle = default;
        }
    }
}

public sealed class BgfxResourceSet : DisposableGraphicsResource, IGraphicsResourceSet
{
}
