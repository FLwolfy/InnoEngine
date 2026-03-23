using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxCommandList : DisposableGraphicsResource, IGraphicsCommandList
{
    private const ushort DefaultViewId = 0;
    private static readonly float[] s_identityMatrix =
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1
    ];

    private bool m_isRecording;
    private BgfxRenderTarget? m_renderTarget;
    private ClearValue m_clearValue;
    private GraphicsViewport m_viewport;
    private GraphicsScissorRect m_scissorRect;
    private BgfxRenderPipeline? m_pipeline;
    private BgfxBuffer? m_vertexBuffer;
    private BgfxBuffer? m_indexBuffer;
    private int m_vertexSlot;

    internal bool isRecording => m_isRecording;

    public void Begin()
    {
        m_isRecording = true;
        m_renderTarget = null;
        m_pipeline = null;
        m_vertexBuffer = null;
        m_indexBuffer = null;
        m_vertexSlot = 0;
        m_viewport = default;
        m_scissorRect = default;
    }

    public void End()
    {
        m_isRecording = false;
    }

    public void BeginRenderPass(IGraphicsRenderTarget renderTarget, ClearValue clearValue)
    {
        if (!m_isRecording)
        {
            throw new InvalidOperationException("Call Begin before BeginRenderPass.");
        }

        m_renderTarget = renderTarget as BgfxRenderTarget
            ?? throw new ArgumentException("renderTarget must be BgfxRenderTarget.", nameof(renderTarget));
        m_clearValue = clearValue;

        bgfx.set_view_rect(DefaultViewId, 0, 0, (ushort)m_renderTarget.width, (ushort)m_renderTarget.height);
        bgfx.set_view_clear(
            DefaultViewId,
            (ushort)(bgfx.ClearFlags.Color | bgfx.ClearFlags.Depth),
            PackColor(clearValue),
            clearValue.depth,
            clearValue.stencil);
        unsafe
        {
            fixed (float* matrix = s_identityMatrix)
            {
                bgfx.set_view_transform(DefaultViewId, matrix, matrix);
            }
        }
        bgfx.touch(DefaultViewId);
    }

    public void EndRenderPass()
    {
        m_renderTarget = null;
    }

    public void SetViewport(GraphicsViewport viewport)
    {
        m_viewport = viewport;

        var width = (ushort)Math.Max(1, (int)viewport.width);
        var height = (ushort)Math.Max(1, (int)viewport.height);
        bgfx.set_view_rect(DefaultViewId, (ushort)viewport.x, (ushort)viewport.y, width, height);
    }

    public void SetScissorRect(GraphicsScissorRect rect)
    {
        m_scissorRect = rect;
        bgfx.set_scissor((ushort)rect.x, (ushort)rect.y, (ushort)rect.width, (ushort)rect.height);
    }

    public void SetPipeline(IGraphicsRenderPipeline pipeline)
    {
        m_pipeline = pipeline as BgfxRenderPipeline
            ?? throw new ArgumentException("pipeline must be BgfxRenderPipeline.", nameof(pipeline));
    }

    public void SetVertexBuffer(IGraphicsBuffer vertexBuffer, int slot = 0)
    {
        m_vertexBuffer = vertexBuffer as BgfxBuffer
            ?? throw new ArgumentException("vertexBuffer must be BgfxBuffer.", nameof(vertexBuffer));
        m_vertexSlot = slot;
    }

    public void SetIndexBuffer(IGraphicsBuffer indexBuffer)
    {
        m_indexBuffer = indexBuffer as BgfxBuffer
            ?? throw new ArgumentException("indexBuffer must be BgfxBuffer.", nameof(indexBuffer));
    }

    public void SetResourceSet(int slot, IGraphicsResourceSet resourceSet)
    {
        _ = slot;
        _ = resourceSet;
    }

    public void Draw(int vertexCount, int instanceCount = 1, int firstVertex = 0, int firstInstance = 0)
    {
        _ = instanceCount;
        _ = firstInstance;
        ValidateForDraw();

        var layout = m_pipeline!.inputLayout;
        var vb = m_vertexBuffer!.EnsureVertexBuffer(layout);
        bgfx.set_vertex_buffer((byte)m_vertexSlot, vb, (uint)firstVertex, (uint)vertexCount);

        unsafe
        {
            fixed (float* matrix = s_identityMatrix)
            {
                bgfx.set_transform(matrix, 1);
            }
        }

        bgfx.set_state(m_pipeline.state, 0);
        bgfx.submit(DefaultViewId, m_pipeline.program.handle, 0, 0);
    }

    public void DrawIndexed(DrawIndexedArguments args)
    {
        ValidateForDraw();

        if (m_indexBuffer is null)
        {
            throw new InvalidOperationException("Index buffer is not bound.");
        }

        var layout = m_pipeline!.inputLayout;
        var vb = m_vertexBuffer!.EnsureVertexBuffer(layout);
        var ib = m_indexBuffer.EnsureIndexBuffer();
        var vertexCount = m_vertexBuffer.GetVertexCount(layout.description.stride);

        bgfx.set_vertex_buffer((byte)m_vertexSlot, vb, 0, vertexCount);
        bgfx.set_index_buffer(ib, (uint)args.firstIndex, (uint)args.indexCount);
        bgfx.set_state(m_pipeline.state, 0);
        bgfx.submit(DefaultViewId, m_pipeline.program.handle, 0, 0);
    }

    private static uint PackColor(ClearValue clearValue)
    {
        static uint ToByte(float value)
        {
            var clamped = Math.Clamp(value, 0.0f, 1.0f);
            return (uint)(clamped * 255.0f + 0.5f);
        }

        var r = ToByte(clearValue.r);
        var g = ToByte(clearValue.g);
        var b = ToByte(clearValue.b);
        var a = ToByte(clearValue.a);
        return (r << 24) | (g << 16) | (b << 8) | a;
    }

    private void ValidateForDraw()
    {
        if (m_pipeline is null)
        {
            throw new InvalidOperationException("Pipeline is not bound.");
        }

        if (m_vertexBuffer is null)
        {
            throw new InvalidOperationException("Vertex buffer is not bound.");
        }
    }
}
