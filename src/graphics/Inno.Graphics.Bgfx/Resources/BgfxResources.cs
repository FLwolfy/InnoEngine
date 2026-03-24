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

public sealed class BgfxSampler : DisposableGraphicsResource, IGraphicsSampler
{
    public BgfxSampler(SamplerDescription description)
    {
        this.description = description ?? throw new ArgumentNullException(nameof(description));
    }

    public SamplerDescription description { get; }
}

public sealed class BgfxRenderTarget : DisposableGraphicsResource, IGraphicsRenderTarget
{
    private bgfx.FrameBufferHandle m_frameBufferHandle = new() { idx = ushort.MaxValue };
    private readonly List<bgfx.TextureHandle> m_attachmentHandles = [];
    private readonly List<IGraphicsTexture> m_colorAttachments = [];
    private IGraphicsTexture? m_depthAttachment;

    public unsafe BgfxRenderTarget(GraphicsRenderTargetDescription description)
    {
        width = description.width;
        height = description.height;

        if (description.useBackbuffer || width <= 0 || height <= 0)
        {
            return;
        }

        foreach (var colorFormat in description.colorFormats)
        {
            var handle = bgfx.create_texture_2d(
                (ushort)width,
                (ushort)height,
                false,
                1,
                BgfxFormatConverter.ToBgfxTextureFormat(colorFormat),
                (ulong)bgfx.TextureFlags.Rt,
                null,
                0);
            if (handle.Valid)
            {
                m_attachmentHandles.Add(handle);
                m_colorAttachments.Add(new BgfxTexture(width, height, colorFormat, handle, ownsHandle: false));
            }
        }

        if (description.depthFormat is PixelFormat depthFormat)
        {
            var depthHandle = bgfx.create_texture_2d(
                (ushort)width,
                (ushort)height,
                false,
                1,
                BgfxFormatConverter.ToBgfxTextureFormat(depthFormat),
                (ulong)bgfx.TextureFlags.Rt,
                null,
                0);
            if (depthHandle.Valid)
            {
                m_attachmentHandles.Add(depthHandle);
                m_depthAttachment = new BgfxTexture(width, height, depthFormat, depthHandle, ownsHandle: false);
            }
        }

        if (m_attachmentHandles.Count > 0)
        {
            unsafe
            {
                fixed (bgfx.TextureHandle* handles = m_attachmentHandles.ToArray())
                {
                    m_frameBufferHandle = bgfx.create_frame_buffer_from_handles((byte)m_attachmentHandles.Count, handles, true);
                }
            }
        }
    }

    public int width { get; }

    public int height { get; }

    public IReadOnlyList<IGraphicsTexture> colorAttachments => m_colorAttachments;

    public IGraphicsTexture? depthAttachment => m_depthAttachment;

    internal bgfx.FrameBufferHandle frameBufferHandle => m_frameBufferHandle;

    protected override void Dispose(bool disposing)
    {
        foreach (var colorAttachment in m_colorAttachments)
        {
            colorAttachment.Dispose();
        }
        m_colorAttachments.Clear();
        m_depthAttachment?.Dispose();
        m_depthAttachment = null;

        if (m_frameBufferHandle.Valid)
        {
            bgfx.destroy_frame_buffer(m_frameBufferHandle);
            m_frameBufferHandle = default;
        }

        m_attachmentHandles.Clear();
    }
}

public sealed class BgfxResourceSet : DisposableGraphicsResource, IGraphicsResourceSet
{
    private readonly List<TextureBinding> m_textureBindings = [];
    private readonly HashSet<int> m_boundTextureSlots = [];
    private readonly HashSet<ushort> m_boundUniformIds = [];

    public BgfxResourceSet(ResourceSetDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);

        foreach (var binding in description.bindings)
        {
            if (binding.bindingType != GraphicsBindingType.Texture)
            {
                continue;
            }

            if (binding.resource is not BgfxTexture texture)
            {
                continue;
            }

            if (!m_boundTextureSlots.Add(binding.slot))
            {
                continue;
            }

            var uniform = bgfx.create_uniform($"s_tex{binding.slot}", bgfx.UniformType.Sampler, 1);
            if (!m_boundUniformIds.Add(uniform.idx))
            {
                if (uniform.Valid)
                {
                    bgfx.destroy_uniform(uniform);
                }

                continue;
            }

            m_textureBindings.Add(new TextureBinding(binding.slot, uniform, texture));
        }
    }

    internal IReadOnlyList<TextureBinding> textureBindings => m_textureBindings;

    protected override void Dispose(bool disposing)
    {
        foreach (var binding in m_textureBindings)
        {
            if (binding.uniform.Valid)
            {
                bgfx.destroy_uniform(binding.uniform);
            }
        }

        m_textureBindings.Clear();
        m_boundTextureSlots.Clear();
        m_boundUniformIds.Clear();
    }

    internal readonly record struct TextureBinding(int slot, bgfx.UniformHandle uniform, BgfxTexture texture);
}
