using Inno.Graphics;
using Inno.Native.Bgfx;
using System.Runtime.InteropServices;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxBuffer : DisposableGraphicsResource, IGraphicsBuffer
{
    private byte[] m_data = [];
    private bool m_isDirty;
    private bgfx.VertexBufferHandle m_vertexHandle;
    private bgfx.IndexBufferHandle m_indexHandle;

    public BgfxBuffer(BufferDescription description)
    {
        sizeInBytes = description.sizeInBytes;
        usage = description.usage;
    }

    public int sizeInBytes { get; }

    public GraphicsBufferUsage usage { get; }

    public void SetData<T>(ReadOnlySpan<T> data, int destinationOffsetInBytes = 0) where T : unmanaged
    {
        var byteCount = data.Length * Marshal.SizeOf<T>();
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
            m_indexHandle = bgfx.create_index_buffer(mem, 0);
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
    private byte[] m_data = [];

    public BgfxTexture(TextureDescription description)
    {
        width = description.width;
        height = description.height;
        format = description.format;
    }

    public int width { get; }

    public int height { get; }

    public PixelFormat format { get; }

    public void SetData<T>(ReadOnlySpan<T> data, int mipLevel = 0) where T : unmanaged
    {
        m_data = MemoryMarshal.AsBytes(data).ToArray();
    }
}

public sealed class BgfxSampler : DisposableGraphicsResource, IGraphicsSampler
{
}

public sealed class BgfxRenderTarget : DisposableGraphicsResource, IGraphicsRenderTarget
{
    public BgfxRenderTarget(GraphicsRenderTargetDescription description)
    {
        width = description.width;
        height = description.height;
    }

    public int width { get; }

    public int height { get; }
}

public sealed class BgfxResourceSet : DisposableGraphicsResource, IGraphicsResourceSet
{
}
