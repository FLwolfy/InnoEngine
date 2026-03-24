using Inno.Graphics;
using Inno.Native.Bgfx;
using System.Runtime.InteropServices;

namespace Inno.Graphics.Bgfx;

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

