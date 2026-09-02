using System;
using Inno.Native.Bgfx;
using Inno.Rendering;

namespace Inno.Rendering.Bgfx;

internal sealed unsafe class BgfxCommandEncoder : RenderCommandEncoder
{
    private readonly BgfxDevice m_device;
    private readonly bgfx.Encoder* m_encoder;
    private readonly ushort m_viewId;

    private BgfxPipelineResource? m_pipeline;
    private BgfxBufferResource? m_vertexBuffer;
    private BgfxBufferResource? m_indexBuffer;
    private RenderRasterState? m_rasterState;
    private RenderStencilState m_stencilState = RenderStencilState.disabled;
    private bool m_instanceDataBound;
    private int m_firstVertex;
    private int m_firstIndex;

    /// <summary>
    /// Creates a validated bgfx command encoder instance.
    /// </summary>
    /// <param name="device">
    /// The device consumed by bgfx command encoder; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="encoder">
    /// The encoder consumed by bgfx command encoder; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="viewId">
    /// The view id consumed by bgfx command encoder; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public BgfxCommandEncoder(BgfxDevice device, bgfx.Encoder* encoder, ushort viewId)
    {
        m_device = device;
        m_encoder = encoder;
        m_viewId = viewId;
    }

    /// <summary>
    /// Binds the graphics pipeline used by subsequent draw commands.
    /// </summary>
    /// <param name="pipeline">
    /// The pipeline consumed by bind graphics pipeline; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindGraphicsPipeline(GraphicsPipelineHandle pipeline)
    {
        BgfxPipelineResource resource = m_device.ResolvePipeline(pipeline);
        if (resource.compute)
        {
            throw new ArgumentException("A compute program cannot be bound as a graphics pipeline.", nameof(pipeline));
        }

        m_pipeline = resource;
        m_rasterState = null;
        m_stencilState = RenderStencilState.disabled;
        m_instanceDataBound = false;
    }

    /// <summary>
    /// Binds the compute pipeline used by subsequent dispatch commands.
    /// </summary>
    /// <param name="pipeline">
    /// The pipeline consumed by bind compute pipeline; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindComputePipeline(ComputePipelineHandle pipeline)
    {
        BgfxPipelineResource resource = m_device.ResolvePipeline(pipeline);
        if (!resource.compute)
        {
            throw new ArgumentException("A graphics program cannot be bound as a compute pipeline.", nameof(pipeline));
        }

        m_pipeline = resource;
    }

    /// <summary>
    /// Binds a texture resource to the requested shader binding.
    /// </summary>
    /// <param name="binding">
    /// The binding consumed by bind texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="texture">
    /// The texture consumed by bind texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="sampler">
    /// The sampler consumed by bind texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindTexture(
        RenderBindingId binding,
        RenderTextureHandle texture,
        RenderSamplerState sampler)
        => BindTexture(binding, m_device.ResolveTexture(texture), sampler);

    /// <summary>
    /// Binds a texture resource to the requested shader binding.
    /// </summary>
    /// <param name="binding">
    /// The binding consumed by bind texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="texture">
    /// The texture consumed by bind texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="sampler">
    /// The sampler consumed by bind texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindTexture(
        RenderBindingId binding,
        PersistentTextureHandle texture,
        RenderSamplerState sampler)
        => BindTexture(binding, m_device.ResolveTexture(texture), sampler);

    /// <summary>
    /// Binds a writable texture resource to the requested shader binding.
    /// </summary>
    /// <param name="binding">
    /// The binding consumed by bind storage texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="texture">
    /// The texture consumed by bind storage texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="mipLevel">
    /// The mip level consumed by bind storage texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindStorageTexture(
        RenderBindingId binding,
        RenderTextureHandle texture,
        int mipLevel = 0)
        => BindStorageTexture(
            binding,
            m_device.ResolveTexture(texture),
            m_device.ResolveTextureDescriptor(texture),
            mipLevel);

    /// <summary>
    /// Binds a writable texture resource to the requested shader binding.
    /// </summary>
    /// <param name="binding">
    /// The binding consumed by bind storage texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="texture">
    /// The texture consumed by bind storage texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="mipLevel">
    /// The mip level consumed by bind storage texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindStorageTexture(
        RenderBindingId binding,
        PersistentTextureHandle texture,
        int mipLevel = 0)
        => BindStorageTexture(
            binding,
            m_device.ResolveTexture(texture),
            m_device.ResolveTextureDescriptor(texture),
            mipLevel);

    /// <summary>
    /// Binds a buffer resource to the requested shader binding.
    /// </summary>
    /// <param name="binding">
    /// The binding consumed by bind buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="buffer">
    /// The buffer consumed by bind buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindBuffer(RenderBindingId binding, RenderBufferHandle buffer)
        => BindBuffer(binding, m_device.ResolveBuffer(buffer));

    /// <summary>
    /// Binds a buffer resource to the requested shader binding.
    /// </summary>
    /// <param name="binding">
    /// The binding consumed by bind buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="buffer">
    /// The buffer consumed by bind buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindBuffer(RenderBindingId binding, PersistentBufferHandle buffer)
        => BindBuffer(binding, m_device.ResolveBuffer(buffer));

    /// <summary>
    /// Updates the uniform state and applies the resulting invariants.
    /// </summary>
    /// <param name="binding">
    /// The binding consumed by set uniform; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    public override void SetUniform(RenderBindingId binding, ReadOnlySpan<byte> value)
    {
        BgfxShaderBindingResource resource = ResolveBinding(binding, RenderShaderBindingKind.Uniform);
        int elementSize = resource.descriptor.uniformType switch
        {
            RenderUniformType.Vector4 => 4 * sizeof(float),
            RenderUniformType.Matrix3x3 => 9 * sizeof(float),
            RenderUniformType.Matrix4x4 => 16 * sizeof(float),
            _ => throw new ArgumentOutOfRangeException(nameof(binding))
        };
        int expectedSize = checked(elementSize * resource.descriptor.count);
        if (value.Length != expectedSize)
        {
            throw new ArgumentException(
                $"Uniform '{binding.value}' requires exactly {expectedSize} bytes.",
                nameof(value));
        }

        fixed (byte* data = value)
        {
            bgfx.encoder_set_uniform(
                m_encoder,
                resource.uniform,
                data,
                checked((ushort)resource.descriptor.count));
        }
    }

    /// <summary>
    /// Updates the transform state and applies the resulting invariants.
    /// </summary>
    /// <param name="columnMajorMatrix">
    /// The column major matrix consumed by set transform; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void SetTransform(ReadOnlySpan<float> columnMajorMatrix)
    {
        if (columnMajorMatrix.Length != 16)
        {
            throw new ArgumentException("An object transform requires exactly sixteen values.", nameof(columnMajorMatrix));
        }

        fixed (float* matrix = columnMajorMatrix)
        {
            bgfx.encoder_set_transform(m_encoder, matrix, 1);
        }
    }

    /// <summary>
    /// Updates the raster state state and applies the resulting invariants.
    /// </summary>
    /// <param name="state">
    /// The lifecycle or domain state applied by this operation.
    /// </param>
    public override void SetRasterState(RenderRasterState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _ = RequireGraphicsPipeline();
        if (state.blend.alphaToCoverage
            && !m_device.capabilities.Supports(GraphicsFeature.AlphaToCoverage))
        {
            throw new NotSupportedException(
                "The active graphics backend does not support alpha-to-coverage rasterization.");
        }
        m_rasterState = state;
    }

    /// <summary>
    /// Updates the stencil state and applies the resulting invariants.
    /// </summary>
    /// <param name="state">
    /// The lifecycle or domain state applied by this operation.
    /// </param>
    public override void SetStencil(RenderStencilState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _ = RequireGraphicsPipeline();
        if (state.enabled && state.writeMask != byte.MaxValue)
        {
            throw new NotSupportedException(
                "BGFX does not expose an independent dynamic stencil write mask.");
        }
        m_stencilState = state;
    }

    /// <summary>
    /// Updates the viewport state and applies the resulting invariants.
    /// </summary>
    /// <param name="x">
    /// The horizontal or first component.
    /// </param>
    /// <param name="y">
    /// The vertical or second component.
    /// </param>
    /// <param name="width">
    /// The width in logical units or pixels required by this operation.
    /// </param>
    /// <param name="height">
    /// The height in logical units or pixels required by this operation.
    /// </param>
    public override void SetViewport(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        bgfx.set_view_rect(
            m_viewId,
            checked((ushort)x),
            checked((ushort)y),
            checked((ushort)width),
            checked((ushort)height));
    }

    /// <summary>
    /// Updates the scissor state and applies the resulting invariants.
    /// </summary>
    /// <param name="x">
    /// The horizontal or first component.
    /// </param>
    /// <param name="y">
    /// The vertical or second component.
    /// </param>
    /// <param name="width">
    /// The width in logical units or pixels required by this operation.
    /// </param>
    /// <param name="height">
    /// The height in logical units or pixels required by this operation.
    /// </param>
    public override void SetScissor(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        bgfx.encoder_set_scissor(
            m_encoder,
            checked((ushort)x),
            checked((ushort)y),
            checked((ushort)width),
            checked((ushort)height));
    }

    /// <summary>
    /// Binds a vertex buffer and its first vertex for subsequent draws.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by bind vertex buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstVertex">
    /// The first vertex consumed by bind vertex buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindVertexBuffer(RenderBufferHandle buffer, int firstVertex = 0)
        => BindVertexBuffer(m_device.ResolveBuffer(buffer), firstVertex);

    /// <summary>
    /// Binds a vertex buffer and its first vertex for subsequent draws.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by bind vertex buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstVertex">
    /// The first vertex consumed by bind vertex buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindVertexBuffer(PersistentBufferHandle buffer, int firstVertex = 0)
        => BindVertexBuffer(m_device.ResolveBuffer(buffer), firstVertex);

    /// <summary>
    /// Binds an index buffer and its first index for subsequent indexed draws.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by bind index buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstIndex">
    /// The first index consumed by bind index buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindIndexBuffer(RenderBufferHandle buffer, int firstIndex = 0)
        => BindIndexBuffer(m_device.ResolveBuffer(buffer), firstIndex);

    /// <summary>
    /// Binds an index buffer and its first index for subsequent indexed draws.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by bind index buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstIndex">
    /// The first index consumed by bind index buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindIndexBuffer(PersistentBufferHandle buffer, int firstIndex = 0)
        => BindIndexBuffer(m_device.ResolveBuffer(buffer), firstIndex);

    /// <summary>
    /// Binds per-instance data for subsequent instanced draws.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by bind instance buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstInstance">
    /// The first instance consumed by bind instance buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="instanceCount">
    /// The instance count consumed by bind instance buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindInstanceBuffer(
        RenderBufferHandle buffer,
        int firstInstance,
        int instanceCount)
        => BindInstanceBuffer(m_device.ResolveBuffer(buffer), firstInstance, instanceCount);

    /// <summary>
    /// Binds per-instance data for subsequent instanced draws.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by bind instance buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstInstance">
    /// The first instance consumed by bind instance buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="instanceCount">
    /// The instance count consumed by bind instance buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindInstanceBuffer(
        PersistentBufferHandle buffer,
        int firstInstance,
        int instanceCount)
        => BindInstanceBuffer(m_device.ResolveBuffer(buffer), firstInstance, instanceCount);

    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="vertexCount">
    /// The vertex count consumed by draw; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="instanceCount">
    /// The instance count consumed by draw; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void Draw(int vertexCount, int instanceCount = 1)
    {
        BgfxPipelineResource pipeline = RequireGraphicsPipeline();
        ValidateDrawCounts(vertexCount, instanceCount);
        if (m_vertexBuffer is null)
        {
            throw new InvalidOperationException(
                "A direct non-procedural draw requires a bound vertex buffer. Use DrawProcedural explicitly otherwise.");
        }

        SetVertexBuffer(pipeline, m_vertexBuffer, m_firstVertex, vertexCount);
        Submit(pipeline, instanceCount);
    }

    /// <summary>
    /// Renders the procedural presentation for the current editor frame.
    /// </summary>
    /// <param name="vertexCount">
    /// The vertex count consumed by draw procedural; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="instanceCount">
    /// The instance count consumed by draw procedural; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void DrawProcedural(int vertexCount, int instanceCount = 1)
    {
        BgfxPipelineResource pipeline = RequireGraphicsPipeline();
        if (!m_device.capabilities.Supports(GraphicsFeature.ProceduralDraw))
        {
            throw new NotSupportedException(
                "The active graphics backend does not support procedural vertex-ID draws.");
        }
        ValidateDrawCounts(vertexCount, instanceCount);
        bgfx.encoder_set_vertex_count(m_encoder, checked((uint)vertexCount));
        Submit(pipeline, instanceCount);
    }

    /// <summary>
    /// Renders the indexed presentation for the current editor frame.
    /// </summary>
    /// <param name="indexCount">
    /// The index count consumed by draw indexed; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="instanceCount">
    /// The instance count consumed by draw indexed; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void DrawIndexed(int indexCount, int instanceCount = 1)
    {
        BgfxPipelineResource pipeline = RequireGraphicsPipeline();
        ValidateDrawCounts(indexCount, instanceCount);
        if (m_vertexBuffer is null)
        {
            throw new InvalidOperationException("An indexed draw requires a bound vertex buffer.");
        }

        if (m_indexBuffer is null)
        {
            throw new InvalidOperationException("An indexed draw requires a bound index buffer.");
        }

        SetVertexBuffer(
            pipeline,
            m_vertexBuffer,
            m_firstVertex,
            m_vertexBuffer.descriptor.elementCount - m_firstVertex);
        SetIndexBuffer(m_indexBuffer, m_firstIndex, indexCount);
        Submit(pipeline, instanceCount);
    }

    /// <summary>
    /// Renders the indirect presentation for the current editor frame.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by draw indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstCommand">
    /// The first command consumed by draw indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="commandCount">
    /// The command count consumed by draw indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void DrawIndirect(
        RenderBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1)
        => DrawIndirect(m_device.ResolveBuffer(buffer), firstCommand, commandCount);

    /// <summary>
    /// Renders the indirect presentation for the current editor frame.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by draw indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstCommand">
    /// The first command consumed by draw indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="commandCount">
    /// The command count consumed by draw indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void DrawIndirect(
        PersistentBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1)
        => DrawIndirect(m_device.ResolveBuffer(buffer), firstCommand, commandCount);

    /// <summary>
    /// Dispatches compute work using the supplied thread-group dimensions.
    /// </summary>
    /// <param name="groupCountX">
    /// The group count x consumed by dispatch; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="groupCountY">
    /// The group count y consumed by dispatch; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="groupCountZ">
    /// The group count z consumed by dispatch; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void Dispatch(int groupCountX, int groupCountY = 1, int groupCountZ = 1)
    {
        BgfxPipelineResource pipeline = RequireComputePipeline();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupCountX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupCountY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupCountZ);
        bgfx.encoder_dispatch(
            m_encoder,
            m_viewId,
            pipeline.program,
            checked((uint)groupCountX),
            checked((uint)groupCountY),
            checked((uint)groupCountZ),
            checked((byte)bgfx.DiscardFlags.All));
        m_device.RecordDispatch();
    }

    /// <summary>
    /// Dispatches compute work using dimensions read from the supplied indirect buffer.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by dispatch indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstCommand">
    /// The first command consumed by dispatch indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="commandCount">
    /// The command count consumed by dispatch indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void DispatchIndirect(
        RenderBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1)
        => DispatchIndirect(m_device.ResolveBuffer(buffer), firstCommand, commandCount);

    /// <summary>
    /// Dispatches compute work using dimensions read from the supplied indirect buffer.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by dispatch indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstCommand">
    /// The first command consumed by dispatch indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="commandCount">
    /// The command count consumed by dispatch indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void DispatchIndirect(
        PersistentBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1)
        => DispatchIndirect(m_device.ResolveBuffer(buffer), firstCommand, commandCount);

    /// <summary>
    /// Copies texture without transferring ownership of the source state.
    /// </summary>
    /// <param name="source">
    /// The source value or location read by this operation.
    /// </param>
    /// <param name="destination">
    /// The destination that receives the completed result.
    /// </param>
    public override void CopyTexture(RenderTextureHandle source, RenderTextureHandle destination)
    {
        RequireTextureBlit();
        RenderTextureDescriptor sourceDescriptor = m_device.ResolveTextureDescriptor(source);
        RenderTextureDescriptor destinationDescriptor = m_device.ResolveTextureDescriptor(destination);
        ValidateCompleteTextureCopy(sourceDescriptor, destinationDescriptor);
        bgfx.TextureHandle sourceTexture = m_device.ResolveTexture(source);
        bgfx.TextureHandle destinationTexture = m_device.ResolveTexture(destination);
        for (int mipLevel = 0; mipLevel < sourceDescriptor.mipCount; mipLevel++)
        {
            ushort width = checked((ushort)Math.Max(1, sourceDescriptor.width >> mipLevel));
            ushort height = checked((ushort)Math.Max(1, sourceDescriptor.height >> mipLevel));
            int layerCount = sourceDescriptor.GetSubresourceLayerCount(mipLevel);
            for (int layer = 0; layer < layerCount; layer++)
            {
                bgfx.encoder_blit(
                    m_encoder,
                    m_viewId,
                    destinationTexture,
                    checked((byte)mipLevel),
                    0,
                    0,
                    checked((ushort)layer),
                    sourceTexture,
                    checked((byte)mipLevel),
                    0,
                    0,
                    checked((ushort)layer),
                    width,
                    height,
                    1);
            }
        }
    }

    /// <summary>
    /// Copies the requested texture region into the destination texture resource.
    /// </summary>
    /// <param name="source">
    /// The source value or location read by this operation.
    /// </param>
    /// <param name="sourceRegion">
    /// The source region consumed by blit texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="destination">
    /// The destination that receives the completed result.
    /// </param>
    /// <param name="destinationRegion">
    /// The destination region consumed by blit texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BlitTexture(
        RenderTextureHandle source,
        RenderTextureRegion sourceRegion,
        RenderTextureHandle destination,
        RenderTextureRegion destinationRegion)
    {
        RequireTextureBlit();
        RenderTextureDescriptor sourceDescriptor = m_device.ResolveTextureDescriptor(source);
        RenderTextureDescriptor destinationDescriptor = m_device.ResolveTextureDescriptor(destination);
        ValidateTextureRegion(sourceDescriptor, sourceRegion, nameof(sourceRegion));
        ValidateTextureRegion(destinationDescriptor, destinationRegion, nameof(destinationRegion));
        ValidateTextureCopyFormats(sourceDescriptor, destinationDescriptor);
        if (sourceRegion.width != destinationRegion.width
            || sourceRegion.height != destinationRegion.height
            || sourceRegion.depth != destinationRegion.depth)
        {
            throw new ArgumentException("BGFX blit source and destination extents must match.");
        }
        bgfx.encoder_blit(
            m_encoder,
            m_viewId,
            m_device.ResolveTexture(destination),
            checked((byte)destinationRegion.mip),
            checked((ushort)destinationRegion.x),
            checked((ushort)destinationRegion.y),
            checked((ushort)destinationRegion.layer),
            m_device.ResolveTexture(source),
            checked((byte)sourceRegion.mip),
            checked((ushort)sourceRegion.x),
            checked((ushort)sourceRegion.y),
            checked((ushort)sourceRegion.layer),
            checked((ushort)sourceRegion.width),
            checked((ushort)sourceRegion.height),
            checked((ushort)sourceRegion.depth));
    }

    /// <summary>
    /// Copies buffer without transferring ownership of the source state.
    /// </summary>
    /// <param name="source">
    /// The source value or location read by this operation.
    /// </param>
    /// <param name="destination">
    /// The destination that receives the completed result.
    /// </param>
    public override void CopyBuffer(RenderBufferHandle source, RenderBufferHandle destination)
    {
        _ = source;
        _ = destination;
        throw new NotSupportedException(
            "BGFX has no general buffer-copy command; use a Plugin compute pass on supported devices.");
    }

    private void RequireTextureBlit()
    {
        if (!m_device.capabilities.Supports(GraphicsFeature.TextureBlit))
        {
            throw new NotSupportedException("The active backend does not support texture blits.");
        }
    }

    private static void ValidateCompleteTextureCopy(
        RenderTextureDescriptor source,
        RenderTextureDescriptor destination)
    {
        ValidateTextureCopyFormats(source, destination);
        if (source.width != destination.width
            || source.height != destination.height
            || source.depth != destination.depth
            || source.dimension != destination.dimension
            || source.mipCount != destination.mipCount
            || source.arrayLayers != destination.arrayLayers)
        {
            throw new ArgumentException(
                "A complete texture copy requires equal dimensions, mip counts, and array layers.");
        }
    }

    private static void ValidateTextureCopyFormats(
        RenderTextureDescriptor source,
        RenderTextureDescriptor destination)
    {
        if (source.format != destination.format || source.sampleCount != destination.sampleCount)
        {
            throw new ArgumentException("Texture copies require equal formats and sample counts.");
        }

        if (source.sampleCount != 1)
        {
            throw new NotSupportedException("BGFX texture blits do not copy multisampled resources.");
        }

        if ((source.usage & RenderTextureUsage.CopySource) == 0
            || (destination.usage & RenderTextureUsage.CopyDestination) == 0)
        {
            throw new ArgumentException(
                "Texture copies require CopySource and CopyDestination usage respectively.");
        }
    }

    private static void ValidateTextureRegion(
        RenderTextureDescriptor descriptor,
        RenderTextureRegion region,
        string parameterName)
    {
        if (region.mip >= descriptor.mipCount)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Texture mip level is outside the descriptor.");
        }

        int mipWidth = Math.Max(1, descriptor.width >> region.mip);
        int mipHeight = Math.Max(1, descriptor.height >> region.mip);
        if (region.x > mipWidth - region.width
            || region.y > mipHeight - region.height
            || region.layer > descriptor.GetSubresourceLayerCount(region.mip) - region.depth)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Texture region is outside the descriptor.");
        }
    }

    private void BindTexture(
        RenderBindingId binding,
        bgfx.TextureHandle texture,
        RenderSamplerState sampler)
    {
        BgfxShaderBindingResource resource = ResolveBinding(binding, RenderShaderBindingKind.Texture);
        bgfx.encoder_set_texture(
            m_encoder,
            checked((byte)resource.descriptor.slot),
            resource.uniform,
            texture,
            SamplerFlags(sampler));
    }

    private void BindStorageTexture(
        RenderBindingId binding,
        bgfx.TextureHandle texture,
        RenderTextureDescriptor descriptor,
        int mipLevel)
    {
        BgfxShaderBindingResource resource = ResolveBinding(binding, RenderShaderBindingKind.StorageTexture);
        if (!m_device.capabilities.Supports(GraphicsFeature.StorageTexture))
        {
            throw new NotSupportedException(
                "The active graphics backend does not support shader storage textures.");
        }
        if ((descriptor.usage & RenderTextureUsage.Storage) == 0)
        {
            throw new ArgumentException(
                $"Texture bound to '{binding.value}' was not created for storage access.",
                nameof(descriptor));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(mipLevel);
        if (mipLevel >= descriptor.mipCount)
            throw new ArgumentOutOfRangeException(nameof(mipLevel));
        if (!m_device.capabilities.SupportsStorage(descriptor.format, resource.descriptor.storageAccess))
        {
            throw new NotSupportedException(
                $"Texture format '{descriptor.format}' does not support {resource.descriptor.storageAccess} storage access.");
        }

        bgfx.encoder_set_image(
            m_encoder,
            checked((byte)resource.descriptor.slot),
            texture,
            checked((byte)mipLevel),
            StorageAccess(resource.descriptor.storageAccess),
            BgfxCapabilityMapper.ToNativeFormat(descriptor.format));
    }

    private void BindBuffer(RenderBindingId binding, BgfxBufferResource buffer)
    {
        BgfxShaderBindingResource resource = ResolveBinding(binding, RenderShaderBindingKind.StorageBuffer);
        if ((buffer.descriptor.usage & RenderBufferUsage.Storage) == 0)
        {
            throw new ArgumentException(
                $"Buffer bound to '{binding.value}' was not created for storage access.",
                nameof(buffer));
        }

        byte slot = checked((byte)resource.descriptor.slot);
        bgfx.Access access = StorageAccess(resource.descriptor.storageAccess);
        switch (buffer.kind)
        {
            case BgfxBufferKind.Vertex:
                bgfx.encoder_set_compute_vertex_buffer(
                    m_encoder,
                    slot,
                    new bgfx.VertexBufferHandle { idx = buffer.nativeIndex },
                    access);
                break;
            case BgfxBufferKind.Index:
                bgfx.encoder_set_compute_index_buffer(
                    m_encoder,
                    slot,
                    new bgfx.IndexBufferHandle { idx = buffer.nativeIndex },
                    access);
                break;
            case BgfxBufferKind.DynamicVertex:
                bgfx.encoder_set_compute_dynamic_vertex_buffer(
                    m_encoder,
                    slot,
                    new bgfx.DynamicVertexBufferHandle { idx = buffer.nativeIndex },
                    access);
                break;
            case BgfxBufferKind.DynamicIndex:
                bgfx.encoder_set_compute_dynamic_index_buffer(
                    m_encoder,
                    slot,
                    new bgfx.DynamicIndexBufferHandle { idx = buffer.nativeIndex },
                    access);
                break;
            case BgfxBufferKind.Indirect:
                bgfx.encoder_set_compute_indirect_buffer(
                    m_encoder,
                    slot,
                    new bgfx.IndirectBufferHandle { idx = buffer.nativeIndex },
                    access);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(buffer));
        }
    }

    private void BindVertexBuffer(BgfxBufferResource buffer, int firstVertex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstVertex);
        if ((buffer.descriptor.usage & RenderBufferUsage.Vertex) == 0)
        {
            throw new ArgumentException("The buffer was not created for vertex input.", nameof(buffer));
        }

        if (firstVertex >= buffer.descriptor.elementCount)
        {
            throw new ArgumentOutOfRangeException(nameof(firstVertex));
        }

        m_vertexBuffer = buffer;
        m_firstVertex = firstVertex;
    }

    private void BindIndexBuffer(BgfxBufferResource buffer, int firstIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstIndex);
        if ((buffer.descriptor.usage & RenderBufferUsage.Index) == 0)
        {
            throw new ArgumentException("The buffer was not created for index input.", nameof(buffer));
        }

        if (firstIndex >= buffer.descriptor.elementCount)
        {
            throw new ArgumentOutOfRangeException(nameof(firstIndex));
        }

        m_indexBuffer = buffer;
        m_firstIndex = firstIndex;
    }

    private void BindInstanceBuffer(BgfxBufferResource buffer, int firstInstance, int instanceCount)
    {
        if (!m_device.capabilities.Supports(GraphicsFeature.Instancing))
            throw new NotSupportedException("The active graphics backend does not support instancing.");
        ArgumentOutOfRangeException.ThrowIfNegative(firstInstance);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(instanceCount);
        ValidateRange(firstInstance, instanceCount, buffer.descriptor.elementCount, nameof(instanceCount));
        switch (buffer.kind)
        {
            case BgfxBufferKind.Vertex:
                bgfx.encoder_set_instance_data_from_vertex_buffer(
                    m_encoder,
                    new bgfx.VertexBufferHandle { idx = buffer.nativeIndex },
                    checked((uint)firstInstance),
                    checked((uint)instanceCount));
                break;
            case BgfxBufferKind.DynamicVertex:
                bgfx.encoder_set_instance_data_from_dynamic_vertex_buffer(
                    m_encoder,
                    new bgfx.DynamicVertexBufferHandle { idx = buffer.nativeIndex },
                    checked((uint)firstInstance),
                    checked((uint)instanceCount));
                break;
            default:
                throw new ArgumentException("Instance data requires a vertex-compatible buffer.", nameof(buffer));
        }
        m_instanceDataBound = true;
    }

    private void DrawIndirect(BgfxBufferResource buffer, int firstCommand, int commandCount)
    {
        BgfxPipelineResource pipeline = RequireGraphicsPipeline();
        ValidateIndirect(buffer, firstCommand, commandCount);
        if (m_vertexBuffer is not null)
        {
            SetVertexBuffer(
                pipeline,
                m_vertexBuffer,
                m_firstVertex,
                m_vertexBuffer.descriptor.elementCount - m_firstVertex);
        }
        else if (!m_device.capabilities.Supports(GraphicsFeature.ProceduralDraw))
        {
            throw new NotSupportedException(
                "An indirect draw without a vertex buffer requires procedural draw capability.");
        }
        if (m_indexBuffer is not null)
        {
            SetIndexBuffer(
                m_indexBuffer,
                m_firstIndex,
                m_indexBuffer.descriptor.elementCount - m_firstIndex);
        }
        SetDrawState(pipeline);
        bgfx.encoder_submit_indirect(
            m_encoder,
            m_viewId,
            pipeline.program,
            new bgfx.IndirectBufferHandle { idx = buffer.nativeIndex },
            checked((uint)firstCommand),
            checked((uint)commandCount),
            0,
            checked((byte)bgfx.DiscardFlags.All));
        m_device.RecordDraw(commandCount);
        m_instanceDataBound = false;
    }

    private void DispatchIndirect(BgfxBufferResource buffer, int firstCommand, int commandCount)
    {
        BgfxPipelineResource pipeline = RequireComputePipeline();
        ValidateIndirect(buffer, firstCommand, commandCount);
        bgfx.encoder_dispatch_indirect(
            m_encoder,
            m_viewId,
            pipeline.program,
            new bgfx.IndirectBufferHandle { idx = buffer.nativeIndex },
            checked((uint)firstCommand),
            checked((uint)commandCount),
            checked((byte)bgfx.DiscardFlags.All));
        m_device.RecordDispatch(commandCount);
    }

    private void ValidateIndirect(BgfxBufferResource buffer, int firstCommand, int commandCount)
    {
        if (!m_device.capabilities.Supports(GraphicsFeature.Indirect))
            throw new NotSupportedException("The active graphics backend does not support indirect commands.");
        ArgumentOutOfRangeException.ThrowIfNegative(firstCommand);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandCount);
        ValidateRange(firstCommand, commandCount, buffer.descriptor.elementCount, nameof(commandCount));
        if (buffer.kind != BgfxBufferKind.Indirect)
            throw new ArgumentException("The buffer was not created for indirect commands.", nameof(buffer));
    }

    private BgfxShaderBindingResource ResolveBinding(
        RenderBindingId binding,
        RenderShaderBindingKind requiredKind)
    {
        if (!binding.isValid)
        {
            throw new ArgumentException("A stable shader binding name is required.", nameof(binding));
        }

        BgfxPipelineResource pipeline = m_pipeline
            ?? throw new InvalidOperationException("A pipeline must be bound before shader resources.");
        if (!pipeline.bindings.TryGetValue(binding.value, out BgfxShaderBindingResource? resource))
        {
            throw new ArgumentException(
                $"Pipeline does not declare shader binding '{binding.value}'.",
                nameof(binding));
        }

        if (resource.descriptor.kind != requiredKind)
        {
            throw new ArgumentException(
                $"Shader binding '{binding.value}' is {resource.descriptor.kind}, not {requiredKind}.",
                nameof(binding));
        }

        return resource;
    }

    private BgfxPipelineResource RequireGraphicsPipeline()
    {
        if (m_pipeline is null || m_pipeline.compute)
        {
            throw new InvalidOperationException("A graphics pipeline must be bound before drawing.");
        }

        return m_pipeline;
    }

    private BgfxPipelineResource RequireComputePipeline()
    {
        if (m_pipeline is null || !m_pipeline.compute)
        {
            throw new InvalidOperationException("A compute pipeline must be bound before dispatch.");
        }

        return m_pipeline;
    }

    private void SetVertexBuffer(
        BgfxPipelineResource pipeline,
        BgfxBufferResource buffer,
        int firstVertex,
        int vertexCount)
    {
        ValidateRange(firstVertex, vertexCount, buffer.descriptor.elementCount, nameof(vertexCount));
        if (pipeline.vertexLayout is null || !pipeline.vertexLayoutHandle.Valid)
        {
            throw new InvalidOperationException(
                "A graphics pipeline with procedural vertices cannot bind a vertex buffer.");
        }

        if (buffer.vertexLayout is not null && !buffer.vertexLayout.Equals(pipeline.vertexLayout))
        {
            throw new InvalidOperationException("Vertex buffer layout does not match the bound graphics pipeline.");
        }

        uint first = checked((uint)firstVertex);
        uint count = checked((uint)vertexCount);
        if (buffer.kind == BgfxBufferKind.Vertex)
        {
            bgfx.VertexBufferHandle handle = new() { idx = buffer.nativeIndex };
            if (buffer.vertexLayout is null)
            {
                bgfx.encoder_set_vertex_buffer_with_layout(
                    m_encoder,
                    0,
                    handle,
                    first,
                    count,
                    pipeline.vertexLayoutHandle);
            }
            else
            {
                bgfx.encoder_set_vertex_buffer(m_encoder, 0, handle, first, count);
            }

            return;
        }

        if (buffer.kind == BgfxBufferKind.DynamicVertex)
        {
            bgfx.DynamicVertexBufferHandle handle = new() { idx = buffer.nativeIndex };
            if (buffer.vertexLayout is null)
            {
                bgfx.encoder_set_dynamic_vertex_buffer_with_layout(
                    m_encoder,
                    0,
                    handle,
                    first,
                    count,
                    pipeline.vertexLayoutHandle);
            }
            else
            {
                bgfx.encoder_set_dynamic_vertex_buffer(m_encoder, 0, handle, first, count);
            }

            return;
        }

        throw new InvalidOperationException("The bound resource is not a vertex buffer.");
    }

    private void SetIndexBuffer(BgfxBufferResource buffer, int firstIndex, int indexCount)
    {
        ValidateRange(firstIndex, indexCount, buffer.descriptor.elementCount, nameof(indexCount));
        uint first = checked((uint)firstIndex);
        uint count = checked((uint)indexCount);
        if (buffer.kind == BgfxBufferKind.Index)
        {
            bgfx.encoder_set_index_buffer(
                m_encoder,
                new bgfx.IndexBufferHandle { idx = buffer.nativeIndex },
                first,
                count);
            return;
        }

        if (buffer.kind == BgfxBufferKind.DynamicIndex)
        {
            bgfx.encoder_set_dynamic_index_buffer(
                m_encoder,
                new bgfx.DynamicIndexBufferHandle { idx = buffer.nativeIndex },
                first,
                count);
            return;
        }

        throw new InvalidOperationException("The bound resource is not an index buffer.");
    }

    private void Submit(BgfxPipelineResource pipeline, int instanceCount)
    {
        if (instanceCount > 1 && !m_device.capabilities.Supports(GraphicsFeature.Instancing))
            throw new NotSupportedException("The active graphics backend does not support instancing.");
        if (m_instanceDataBound && instanceCount != 1)
        {
            throw new ArgumentException(
                "A draw with bound instance data must use the bound instance count.",
                nameof(instanceCount));
        }
        if (!m_instanceDataBound && instanceCount > 1)
        {
            bgfx.encoder_set_instance_count(m_encoder, checked((uint)instanceCount));
        }

        SetDrawState(pipeline);
        bgfx.encoder_submit(
            m_encoder,
            m_viewId,
            pipeline.program,
            0,
            checked((byte)bgfx.DiscardFlags.All));
        m_device.RecordDraw();
        m_instanceDataBound = false;
    }

    private void SetDrawState(BgfxPipelineResource pipeline)
    {
        RenderRasterState state = m_rasterState ?? pipeline.rasterState!;
        bgfx.encoder_set_state(m_encoder, RasterState(state), state.blend.constantRgba);
        if (!m_stencilState.enabled)
        {
            bgfx.encoder_set_stencil(m_encoder, 0, 0);
            return;
        }
        bgfx.encoder_set_stencil(
            m_encoder,
            StencilFlags(m_stencilState, m_stencilState.front),
            StencilFlags(m_stencilState, m_stencilState.back));
    }

    private static ulong RasterState(RenderRasterState state)
    {
        bgfx.StateFlags flags = bgfx.StateFlags.None;
        if ((state.colorWriteMask & 0x01) != 0)
        {
            flags |= bgfx.StateFlags.WriteR;
        }

        if ((state.colorWriteMask & 0x02) != 0)
        {
            flags |= bgfx.StateFlags.WriteG;
        }

        if ((state.colorWriteMask & 0x04) != 0)
        {
            flags |= bgfx.StateFlags.WriteB;
        }

        if ((state.colorWriteMask & 0x08) != 0)
        {
            flags |= bgfx.StateFlags.WriteA;
        }

        if (state.depthWrite)
        {
            flags |= bgfx.StateFlags.WriteZ;
        }

        flags |= state.depthCompare switch
        {
            RenderDepthCompare.Never => bgfx.StateFlags.DepthTestNever,
            RenderDepthCompare.Less => bgfx.StateFlags.DepthTestLess,
            RenderDepthCompare.Equal => bgfx.StateFlags.DepthTestEqual,
            RenderDepthCompare.LessEqual => bgfx.StateFlags.DepthTestLequal,
            RenderDepthCompare.Greater => bgfx.StateFlags.DepthTestGreater,
            RenderDepthCompare.NotEqual => bgfx.StateFlags.DepthTestNotequal,
            RenderDepthCompare.GreaterEqual => bgfx.StateFlags.DepthTestGequal,
            RenderDepthCompare.Always => bgfx.StateFlags.DepthTestAlways,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

        if (state.frontFace == RenderFrontFace.CounterClockwise)
        {
            flags |= bgfx.StateFlags.FrontCcw;
        }

        flags |= state.cull switch
        {
            RenderCullMode.None => bgfx.StateFlags.None,
            RenderCullMode.Front when state.frontFace == RenderFrontFace.CounterClockwise => bgfx.StateFlags.CullCcw,
            RenderCullMode.Front => bgfx.StateFlags.CullCw,
            RenderCullMode.Back when state.frontFace == RenderFrontFace.CounterClockwise => bgfx.StateFlags.CullCw,
            RenderCullMode.Back => bgfx.StateFlags.CullCcw,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

        if (state.blend.enabled)
        {
            flags = (bgfx.StateFlags)((ulong)flags | BlendFunction(state.blend) | BlendEquation(state.blend));
        }

        if (state.blend.alphaToCoverage)
            flags |= bgfx.StateFlags.BlendAlphaToCoverage;

        if (state.multisampling)
        {
            flags |= bgfx.StateFlags.Msaa;
        }

        flags |= state.topology switch
        {
            RenderPrimitiveTopology.TriangleList => bgfx.StateFlags.None,
            RenderPrimitiveTopology.TriangleStrip => bgfx.StateFlags.PtTristrip,
            RenderPrimitiveTopology.LineList => bgfx.StateFlags.PtLines,
            RenderPrimitiveTopology.LineStrip => bgfx.StateFlags.PtLinestrip,
            RenderPrimitiveTopology.PointList => bgfx.StateFlags.PtPoints,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

        return (ulong)flags;
    }

    private static bgfx.Access StorageAccess(RenderStorageAccess access)
        => access switch
        {
            RenderStorageAccess.Read => bgfx.Access.Read,
            RenderStorageAccess.Write => bgfx.Access.Write,
            RenderStorageAccess.ReadWrite => bgfx.Access.ReadWrite,
            _ => throw new ArgumentOutOfRangeException(nameof(access))
        };

    private static uint SamplerFlags(RenderSamplerState state)
    {
        bgfx.SamplerFlags flags = state.filter switch
        {
            RenderSamplerFilter.Point => bgfx.SamplerFlags.Point,
            RenderSamplerFilter.Linear => bgfx.SamplerFlags.None,
            RenderSamplerFilter.Anisotropic => bgfx.SamplerFlags.MinAnisotropic
                | bgfx.SamplerFlags.MagAnisotropic,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        flags |= AddressFlags(state.addressU, 'U');
        flags |= AddressFlags(state.addressV, 'V');
        flags |= AddressFlags(state.addressW, 'W');
        return (uint)flags;
    }

    private static bgfx.SamplerFlags AddressFlags(RenderSamplerAddressMode mode, char axis)
        => (axis, mode) switch
        {
            (_, RenderSamplerAddressMode.Repeat) => bgfx.SamplerFlags.None,
            ('U', RenderSamplerAddressMode.Mirror) => bgfx.SamplerFlags.UMirror,
            ('U', RenderSamplerAddressMode.Clamp) => bgfx.SamplerFlags.UClamp,
            ('U', RenderSamplerAddressMode.Border) => bgfx.SamplerFlags.UBorder,
            ('V', RenderSamplerAddressMode.Mirror) => bgfx.SamplerFlags.VMirror,
            ('V', RenderSamplerAddressMode.Clamp) => bgfx.SamplerFlags.VClamp,
            ('V', RenderSamplerAddressMode.Border) => bgfx.SamplerFlags.VBorder,
            ('W', RenderSamplerAddressMode.Mirror) => bgfx.SamplerFlags.WMirror,
            ('W', RenderSamplerAddressMode.Clamp) => bgfx.SamplerFlags.WClamp,
            ('W', RenderSamplerAddressMode.Border) => bgfx.SamplerFlags.WBorder,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    private static uint StencilFlags(RenderStencilState state, RenderStencilFaceState face)
    {
        bgfx.StencilFlags flags = (bgfx.StencilFlags)(
            state.reference
            | (uint)(state.readMask << (int)bgfx.StencilFlags.FuncRmaskShift));
        flags |= face.compare switch
        {
            RenderStencilCompare.Never => bgfx.StencilFlags.TestNever,
            RenderStencilCompare.Less => bgfx.StencilFlags.TestLess,
            RenderStencilCompare.Equal => bgfx.StencilFlags.TestEqual,
            RenderStencilCompare.LessEqual => bgfx.StencilFlags.TestLequal,
            RenderStencilCompare.Greater => bgfx.StencilFlags.TestGreater,
            RenderStencilCompare.NotEqual => bgfx.StencilFlags.TestNotequal,
            RenderStencilCompare.GreaterEqual => bgfx.StencilFlags.TestGequal,
            RenderStencilCompare.Always => bgfx.StencilFlags.TestAlways,
            _ => throw new ArgumentOutOfRangeException(nameof(face))
        };
        flags |= StencilOperation(face.fail, 0);
        flags |= StencilOperation(face.depthFail, 1);
        flags |= StencilOperation(face.pass, 2);
        return (uint)flags;
    }

    private static bgfx.StencilFlags StencilOperation(RenderStencilOperation operation, int field)
    {
        int value = operation switch
        {
            RenderStencilOperation.Zero => 0,
            RenderStencilOperation.Keep => 1,
            RenderStencilOperation.Replace => 2,
            RenderStencilOperation.IncrementWrap => 3,
            RenderStencilOperation.IncrementClamp => 4,
            RenderStencilOperation.DecrementWrap => 5,
            RenderStencilOperation.DecrementClamp => 6,
            RenderStencilOperation.Invert => 7,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        int shift = field switch
        {
            0 => (int)bgfx.StencilFlags.OpFailSShift,
            1 => (int)bgfx.StencilFlags.OpFailZShift,
            2 => (int)bgfx.StencilFlags.OpPassZShift,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        return (bgfx.StencilFlags)((uint)value << shift);
    }

    private static ulong BlendFunction(RenderBlendState state)
    {
        ulong colorSource = (ulong)BlendFactor(state.colorSource);
        ulong colorDestination = (ulong)BlendFactor(state.colorDestination);
        ulong alphaSource = (ulong)BlendFactor(state.alphaSource);
        ulong alphaDestination = (ulong)BlendFactor(state.alphaDestination);
        return colorSource
            | (colorDestination << 4)
            | ((alphaSource | (alphaDestination << 4)) << 8);
    }

    private static ulong BlendEquation(RenderBlendState state)
    {
        ulong color = (ulong)BlendEquation(state.colorEquation);
        ulong alpha = (ulong)BlendEquation(state.alphaEquation);
        return color | (alpha << 3);
    }

    private static bgfx.StateFlags BlendFactor(RenderBlendFactor factor)
        => factor switch
        {
            RenderBlendFactor.Zero => bgfx.StateFlags.BlendZero,
            RenderBlendFactor.One => bgfx.StateFlags.BlendOne,
            RenderBlendFactor.SourceColor => bgfx.StateFlags.BlendSrcColor,
            RenderBlendFactor.InverseSourceColor => bgfx.StateFlags.BlendInvSrcColor,
            RenderBlendFactor.SourceAlpha => bgfx.StateFlags.BlendSrcAlpha,
            RenderBlendFactor.InverseSourceAlpha => bgfx.StateFlags.BlendInvSrcAlpha,
            RenderBlendFactor.DestinationAlpha => bgfx.StateFlags.BlendDstAlpha,
            RenderBlendFactor.InverseDestinationAlpha => bgfx.StateFlags.BlendInvDstAlpha,
            RenderBlendFactor.DestinationColor => bgfx.StateFlags.BlendDstColor,
            RenderBlendFactor.InverseDestinationColor => bgfx.StateFlags.BlendInvDstColor,
            RenderBlendFactor.SourceAlphaSaturate => bgfx.StateFlags.BlendSrcAlphaSat,
            RenderBlendFactor.Constant => bgfx.StateFlags.BlendFactor,
            RenderBlendFactor.InverseConstant => bgfx.StateFlags.BlendInvFactor,
            _ => throw new ArgumentOutOfRangeException(nameof(factor))
        };

    private static bgfx.StateFlags BlendEquation(RenderBlendEquation equation)
        => equation switch
        {
            RenderBlendEquation.Add => bgfx.StateFlags.BlendEquationAdd,
            RenderBlendEquation.Subtract => bgfx.StateFlags.BlendEquationSub,
            RenderBlendEquation.ReverseSubtract => bgfx.StateFlags.BlendEquationRevsub,
            RenderBlendEquation.Minimum => bgfx.StateFlags.BlendEquationMin,
            RenderBlendEquation.Maximum => bgfx.StateFlags.BlendEquationMax,
            _ => throw new ArgumentOutOfRangeException(nameof(equation))
        };

    private static void ValidateDrawCounts(int primitiveCount, int instanceCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(primitiveCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(instanceCount);
    }

    private static void ValidateRange(int first, int count, int available, string parameterName)
    {
        if (first < 0 || count <= 0 || first > available - count)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Requested range [{first}, {first + count}) exceeds buffer element count {available}.");
        }
    }

}
