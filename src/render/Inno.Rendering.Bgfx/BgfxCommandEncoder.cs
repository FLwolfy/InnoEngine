using System;
using Inno.Native.Bgfx;
using Inno.Rendering.Core;

namespace Inno.Rendering.Bgfx;

internal sealed unsafe class BgfxCommandEncoder : RenderCommandEncoder
{
    private readonly BgfxDevice m_device;
    private readonly bgfx.Encoder* m_encoder;
    private readonly ushort m_viewId;

    private BgfxPipelineResource? m_pipeline;
    private BgfxBufferResource? m_vertexBuffer;
    private BgfxBufferResource? m_indexBuffer;
    private int m_firstVertex;
    private int m_firstIndex;

    public BgfxCommandEncoder(BgfxDevice device, bgfx.Encoder* encoder, ushort viewId)
    {
        m_device = device;
        m_encoder = encoder;
        m_viewId = viewId;
    }

    public override void BindGraphicsPipeline(GraphicsPipelineHandle pipeline)
    {
        BgfxPipelineResource resource = m_device.ResolvePipeline(pipeline);
        if (resource.compute)
        {
            throw new ArgumentException("A compute program cannot be bound as a graphics pipeline.", nameof(pipeline));
        }

        m_pipeline = resource;
    }

    public override void BindComputePipeline(ComputePipelineHandle pipeline)
    {
        BgfxPipelineResource resource = m_device.ResolvePipeline(pipeline);
        if (!resource.compute)
        {
            throw new ArgumentException("A graphics program cannot be bound as a compute pipeline.", nameof(pipeline));
        }

        m_pipeline = resource;
    }

    public override void BindTexture(RenderBindingId binding, RenderTextureHandle texture)
        => BindTexture(binding, m_device.ResolveTexture(texture));

    public override void BindTexture(RenderBindingId binding, PersistentTextureHandle texture)
        => BindTexture(binding, m_device.ResolveTexture(texture));

    public override void BindBuffer(RenderBindingId binding, RenderBufferHandle buffer)
        => BindBuffer(binding, m_device.ResolveBuffer(buffer));

    public override void BindBuffer(RenderBindingId binding, PersistentBufferHandle buffer)
        => BindBuffer(binding, m_device.ResolveBuffer(buffer));

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

    public override void BindVertexBuffer(RenderBufferHandle buffer, int firstVertex = 0)
        => BindVertexBuffer(m_device.ResolveBuffer(buffer), firstVertex);

    public override void BindVertexBuffer(PersistentBufferHandle buffer, int firstVertex = 0)
        => BindVertexBuffer(m_device.ResolveBuffer(buffer), firstVertex);

    public override void BindIndexBuffer(RenderBufferHandle buffer, int firstIndex = 0)
        => BindIndexBuffer(m_device.ResolveBuffer(buffer), firstIndex);

    public override void BindIndexBuffer(PersistentBufferHandle buffer, int firstIndex = 0)
        => BindIndexBuffer(m_device.ResolveBuffer(buffer), firstIndex);

    public override void Draw(int vertexCount, int instanceCount = 1)
    {
        BgfxPipelineResource pipeline = RequireGraphicsPipeline();
        ValidateDrawCounts(vertexCount, instanceCount);
        if (m_vertexBuffer is null)
        {
            bgfx.encoder_set_vertex_count(m_encoder, checked((uint)vertexCount));
        }
        else
        {
            SetVertexBuffer(pipeline, m_vertexBuffer, m_firstVertex, vertexCount);
        }

        Submit(pipeline, instanceCount);
    }

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
    }

    public override void CopyTexture(RenderTextureHandle source, RenderTextureHandle destination)
    {
        RenderTextureDescriptor sourceDescriptor = m_device.ResolveTextureDescriptor(source);
        RenderTextureDescriptor destinationDescriptor = m_device.ResolveTextureDescriptor(destination);
        ushort width = checked((ushort)Math.Min(sourceDescriptor.width, destinationDescriptor.width));
        ushort height = checked((ushort)Math.Min(sourceDescriptor.height, destinationDescriptor.height));
        bgfx.encoder_blit(
            m_encoder,
            m_viewId,
            m_device.ResolveTexture(destination),
            0,
            0,
            0,
            0,
            m_device.ResolveTexture(source),
            0,
            0,
            0,
            0,
            width,
            height,
            1);
    }

    private void BindTexture(RenderBindingId binding, bgfx.TextureHandle texture)
    {
        BgfxShaderBindingResource resource = ResolveBinding(binding, RenderShaderBindingKind.Texture);
        bgfx.encoder_set_texture(
            m_encoder,
            checked((byte)resource.descriptor.slot),
            resource.uniform,
            texture,
            uint.MaxValue);
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
        bgfx.Access access = resource.descriptor.bufferAccess switch
        {
            RenderBufferBindingAccess.Read => bgfx.Access.Read,
            RenderBufferBindingAccess.Write => bgfx.Access.Write,
            RenderBufferBindingAccess.ReadWrite => bgfx.Access.ReadWrite,
            _ => throw new ArgumentOutOfRangeException(nameof(binding))
        };
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
        if (instanceCount > 1)
        {
            bgfx.encoder_set_instance_count(m_encoder, checked((uint)instanceCount));
        }

        bgfx.encoder_set_state(m_encoder, RasterState(pipeline.rasterState!), 0);
        bgfx.encoder_submit(
            m_encoder,
            m_viewId,
            pipeline.program,
            0,
            checked((byte)bgfx.DiscardFlags.All));
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

        flags = (bgfx.StateFlags)((ulong)flags | state.blend switch
        {
            RenderBlendMode.Opaque => 0UL,
            RenderBlendMode.Alpha => Blend(bgfx.StateFlags.BlendSrcAlpha, bgfx.StateFlags.BlendInvSrcAlpha),
            RenderBlendMode.Additive => Blend(bgfx.StateFlags.BlendSrcAlpha, bgfx.StateFlags.BlendOne),
            RenderBlendMode.Premultiplied => Blend(bgfx.StateFlags.BlendOne, bgfx.StateFlags.BlendInvSrcAlpha),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        });

        if (state.multisampling)
        {
            flags |= bgfx.StateFlags.Msaa;
        }

        return (ulong)flags;
    }

    private static ulong Blend(bgfx.StateFlags source, bgfx.StateFlags destination)
        => (ulong)source | ((ulong)destination << 4);

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
