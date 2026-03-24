using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxCommandList : DisposableGraphicsResource, IGraphicsCommandList
{
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
    private readonly Dictionary<int, BgfxResourceSet> m_resourceSets = new();
    private readonly Dictionary<string, UniformBinding> m_globalUniforms = new(StringComparer.Ordinal);
    private readonly float[] m_viewMatrix = (float[])s_identityMatrix.Clone();
    private readonly float[] m_projectionMatrix = (float[])s_identityMatrix.Clone();
    private readonly float[] m_modelMatrix = (float[])s_identityMatrix.Clone();
    private ushort m_currentViewId;
    private ushort m_nextViewId;

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
        m_resourceSets.Clear();
        Array.Copy(s_identityMatrix, m_viewMatrix, 16);
        Array.Copy(s_identityMatrix, m_projectionMatrix, 16);
        Array.Copy(s_identityMatrix, m_modelMatrix, 16);
        m_currentViewId = 0;
        m_nextViewId = 0;
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
        m_currentViewId = m_nextViewId++;

        if (m_renderTarget.frameBufferHandle.Valid)
        {
            bgfx.set_view_frame_buffer(m_currentViewId, m_renderTarget.frameBufferHandle);
        }
        bgfx.set_view_rect(m_currentViewId, 0, 0, (ushort)m_renderTarget.width, (ushort)m_renderTarget.height);
        bgfx.set_view_clear(
            m_currentViewId,
            (ushort)(bgfx.ClearFlags.Color | bgfx.ClearFlags.Depth),
            PackColor(clearValue),
            clearValue.depth,
            clearValue.stencil);
        unsafe
        {
            fixed (float* view = m_viewMatrix)
            fixed (float* proj = m_projectionMatrix)
            {
                bgfx.set_view_transform(m_currentViewId, view, proj);
            }
        }
        bgfx.touch(m_currentViewId);
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
        bgfx.set_view_rect(m_currentViewId, (ushort)viewport.x, (ushort)viewport.y, width, height);
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
        m_resourceSets[slot] = resourceSet as BgfxResourceSet
            ?? throw new ArgumentException("resourceSet must be BgfxResourceSet.", nameof(resourceSet));
    }

    public void SetViewProjection(ReadOnlySpan<float> view, ReadOnlySpan<float> projection)
    {
        if (view.Length < 16)
        {
            throw new ArgumentException("View matrix must contain 16 float values.", nameof(view));
        }

        if (projection.Length < 16)
        {
            throw new ArgumentException("Projection matrix must contain 16 float values.", nameof(projection));
        }

        view[..16].CopyTo(m_viewMatrix);
        projection[..16].CopyTo(m_projectionMatrix);

        unsafe
        {
            fixed (float* viewPtr = m_viewMatrix)
            fixed (float* projPtr = m_projectionMatrix)
            {
                bgfx.set_view_transform(m_currentViewId, viewPtr, projPtr);
            }
        }
    }

    public void SetModelTransform(ReadOnlySpan<float> model)
    {
        if (model.Length < 16)
        {
            throw new ArgumentException("Model matrix must contain 16 float values.", nameof(model));
        }

        model[..16].CopyTo(m_modelMatrix);
    }

    public void SetGlobalVector4(string name, ReadOnlySpan<float> value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Uniform name cannot be null or whitespace.", nameof(name));
        }

        if (value.Length < 4)
        {
            throw new ArgumentException("Global vector4 uniform value must contain at least 4 floats.", nameof(value));
        }

        if (!m_globalUniforms.TryGetValue(name, out var binding))
        {
            var handle = bgfx.create_uniform(name, bgfx.UniformType.Vec4, 1);
            binding = new UniformBinding(handle, new float[4]);
            m_globalUniforms.Add(name, binding);
        }

        value[..4].CopyTo(binding.values);
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
            fixed (float* matrix = m_modelMatrix)
            {
                bgfx.set_transform(matrix, 1);
            }
        }

        ApplyGlobalUniforms();
        BindResourceSets();
        bgfx.set_state(m_pipeline.state, 0);
        bgfx.submit(m_currentViewId, m_pipeline.program.handle, 0, (byte)bgfx.DiscardFlags.All);
        m_resourceSets.Clear();
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
        unsafe
        {
            fixed (float* matrix = m_modelMatrix)
            {
                bgfx.set_transform(matrix, 1);
            }
        }
        ApplyGlobalUniforms();
        BindResourceSets();
        bgfx.set_state(m_pipeline.state, 0);
        bgfx.submit(m_currentViewId, m_pipeline.program.handle, 0, (byte)bgfx.DiscardFlags.All);
        m_resourceSets.Clear();
    }

    private unsafe void ApplyGlobalUniforms()
    {
        foreach (var entry in m_globalUniforms.Values)
        {
            fixed (float* values = entry.values)
            {
                bgfx.set_uniform(entry.handle, values, 1);
            }
        }
    }

    private void BindResourceSets()
    {
        HashSet<int> boundSlots = [];
        foreach (var resourceSetEntry in m_resourceSets)
        {
            _ = resourceSetEntry.Key;
            var resourceSet = resourceSetEntry.Value;
            foreach (var textureBinding in resourceSet.textureBindings)
            {
                if (!boundSlots.Add(textureBinding.slot))
                {
                    continue;
                }

                bgfx.set_texture(
                    (byte)textureBinding.slot,
                    textureBinding.uniform,
                    textureBinding.texture.handle,
                    uint.MaxValue);
            }
        }
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

    protected override void Dispose(bool disposing)
    {
        foreach (var uniform in m_globalUniforms.Values)
        {
            if (uniform.handle.Valid)
            {
                bgfx.destroy_uniform(uniform.handle);
            }
        }

        m_globalUniforms.Clear();
    }

    private sealed class UniformBinding
    {
        public UniformBinding(bgfx.UniformHandle handle, float[] values)
        {
            this.handle = handle;
            this.values = values;
        }

        public bgfx.UniformHandle handle { get; }

        public float[] values { get; }
    }
}
