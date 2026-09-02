using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Inno.Native.Bgfx;
using Inno.Rendering;

namespace Inno.Rendering.Bgfx;

/// <summary>
/// Implements the backend-neutral render device contract using generation-scoped BGFX resources.
/// </summary>
public sealed unsafe partial class BgfxDevice
{
    private readonly Dictionary<ulong, BgfxBufferResource> m_persistentBuffers = [];
    private readonly Dictionary<ulong, BgfxPipelineResource> m_graphicsPipelines = [];
    private readonly Dictionary<ulong, BgfxPipelineResource> m_computePipelines = [];
    private readonly Dictionary<int, BgfxBufferResource> m_graphBuffers = [];
    private readonly Dictionary<int, BgfxBufferResource> m_transientBufferSlots = [];

    /// <summary>
    /// Creates a buffer using this implementation's validated inputs.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor consumed by create buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="initialData">
    /// The initial data consumed by create buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="name">
    /// The human-readable name used for presentation and diagnostics.
    /// </param>
    /// <returns>
    /// The validated persistent buffer handle that represents the completed operation.
    /// </returns>
    public PersistentBufferHandle CreateBuffer(
        PersistentBufferDescriptor descriptor,
        ReadOnlySpan<byte> initialData,
        string name)
    {
        EnsureFrameSafetyPoint();
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        int expectedSize = checked(descriptor.buffer.elementCount * descriptor.buffer.elementStride);
        if (!initialData.IsEmpty && initialData.Length != expectedSize)
        {
            throw new ArgumentException(
                $"Initial buffer data must contain exactly {expectedSize} bytes.",
                nameof(initialData));
        }

        BgfxBufferResource resource = CreateNativeBuffer(descriptor, initialData, name);
        ulong id = m_nextPersistentId++;
        m_persistentBuffers.Add(id, resource);
        return CreatePersistentBufferHandle(id, generation);
    }

    /// <summary>
    /// Destroys the buffer after all in-flight references have retired.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by destroy buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public void DestroyBuffer(PersistentBufferHandle buffer)
    {
        EnsureFrameSafetyPoint();
        ValidatePersistentHandle(buffer);
        if (!m_persistentBuffers.Remove(GetHandleIdentity(buffer).value, out BgfxBufferResource? resource))
        {
            throw new ArgumentException("Persistent buffer is not active on this device.", nameof(buffer));
        }

        EnqueueDestroy(DeferredResource.ForBuffer(resource));
    }

    /// <summary>
    /// Updates buffer state from the current authoritative inputs.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by update buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="data">
    /// The complete immutable byte payload consumed by this operation.
    /// </param>
    /// <param name="startElement">
    /// The start element consumed by update buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public void UpdateBuffer(
        PersistentBufferHandle buffer,
        ReadOnlySpan<byte> data,
        int startElement = 0)
    {
        EnsureFrameSafetyPoint();
        ArgumentOutOfRangeException.ThrowIfNegative(startElement);
        if (data.IsEmpty)
        {
            throw new ArgumentException("Dynamic buffer updates cannot be empty.", nameof(data));
        }

        BgfxBufferResource resource = ResolveBuffer(buffer);
        if (resource.kind == BgfxBufferKind.Indirect)
        {
            throw new NotSupportedException(
                "BGFX indirect command buffers are populated by compute shaders, not CPU updates.");
        }
        if ((resource.descriptor.usage & RenderBufferUsage.Dynamic) == 0)
        {
            throw new ArgumentException("Only buffers declared Dynamic can be updated.", nameof(buffer));
        }

        if (data.Length % resource.descriptor.elementStride != 0)
        {
            throw new ArgumentException("Update data must contain complete buffer elements.", nameof(data));
        }

        int elementCount = data.Length / resource.descriptor.elementStride;
        if (startElement + elementCount > resource.descriptor.elementCount)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "The update exceeds buffer capacity.");
        }

        bgfx.Memory* memory = Copy(data);
        switch (resource.kind)
        {
            case BgfxBufferKind.DynamicVertex:
                bgfx.update_dynamic_vertex_buffer(
                    new bgfx.DynamicVertexBufferHandle { idx = resource.nativeIndex },
                    checked((uint)startElement),
                    memory);
                break;
            case BgfxBufferKind.DynamicIndex:
                bgfx.update_dynamic_index_buffer(
                    new bgfx.DynamicIndexBufferHandle { idx = resource.nativeIndex },
                    checked((uint)startElement),
                    memory);
                break;
            default:
                throw new InvalidOperationException("A Dynamic descriptor resolved to an immutable BGFX buffer.");
        }
    }

    /// <summary>
    /// Creates a graphics pipeline using this implementation's validated inputs.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor consumed by create graphics pipeline; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="name">
    /// The human-readable name used for presentation and diagnostics.
    /// </param>
    /// <returns>
    /// The validated graphics pipeline handle that represents the completed operation.
    /// </returns>
    public GraphicsPipelineHandle CreateGraphicsPipeline(GraphicsPipelineDescriptor descriptor, string name)
    {
        EnsureFrameSafetyPoint();
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        BgfxPipelineResource resource = CreateGraphicsPipelineResource(descriptor, name);
        ulong id = m_nextPersistentId++;
        m_graphicsPipelines.Add(id, resource);
        return CreateGraphicsPipelineHandle(id, generation);
    }

    /// <summary>
    /// Destroys the graphics pipeline after all in-flight references have retired.
    /// </summary>
    /// <param name="pipeline">
    /// The pipeline consumed by destroy graphics pipeline; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public void DestroyGraphicsPipeline(GraphicsPipelineHandle pipeline)
    {
        EnsureFrameSafetyPoint();
        ValidatePersistentHandle(pipeline);
        if (!m_graphicsPipelines.Remove(GetHandleIdentity(pipeline).value, out BgfxPipelineResource? resource))
        {
            throw new ArgumentException("Graphics pipeline is not active on this device.", nameof(pipeline));
        }

        EnqueuePipelineDestroy(resource);
        if (resource.vertexLayoutHandle.Valid)
        {
            EnqueueDestroy(DeferredResource.ForVertexLayout(resource.vertexLayoutHandle));
        }
    }

    /// <summary>
    /// Creates a compute pipeline using this implementation's validated inputs.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor consumed by create compute pipeline; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="name">
    /// The human-readable name used for presentation and diagnostics.
    /// </param>
    /// <returns>
    /// The validated compute pipeline handle that represents the completed operation.
    /// </returns>
    public ComputePipelineHandle CreateComputePipeline(ComputePipelineDescriptor descriptor, string name)
    {
        EnsureFrameSafetyPoint();
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!capabilities.Supports(GraphicsFeature.Compute))
        {
            throw new NotSupportedException("The active graphics device does not support compute pipelines.");
        }

        BgfxPipelineResource resource = CreateComputePipelineResource(descriptor, name);
        ulong id = m_nextPersistentId++;
        m_computePipelines.Add(id, resource);
        return CreateComputePipelineHandle(id, generation);
    }

    /// <summary>
    /// Destroys the compute pipeline after all in-flight references have retired.
    /// </summary>
    /// <param name="pipeline">
    /// The pipeline consumed by destroy compute pipeline; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public void DestroyComputePipeline(ComputePipelineHandle pipeline)
    {
        EnsureFrameSafetyPoint();
        ValidatePersistentHandle(pipeline);
        if (!m_computePipelines.Remove(GetHandleIdentity(pipeline).value, out BgfxPipelineResource? resource))
        {
            throw new ArgumentException("Compute pipeline is not active on this device.", nameof(pipeline));
        }

        EnqueuePipelineDestroy(resource);
    }

    internal bgfx.TextureHandle ResolveTexture(PersistentTextureHandle texture)
    {
        ValidatePersistentHandle(texture);
        if (!m_persistentTextures.TryGetValue(GetHandleIdentity(texture).value, out bgfx.TextureHandle nativeTexture))
        {
            throw new ArgumentException("Persistent texture is not active on this device.", nameof(texture));
        }

        return nativeTexture;
    }

    internal RenderTextureDescriptor ResolveTextureDescriptor(PersistentTextureHandle texture)
    {
        ValidatePersistentHandle(texture);
        if (!m_persistentTextureDescriptors.TryGetValue(GetHandleIdentity(texture).value, out RenderTextureDescriptor? descriptor))
        {
            throw new ArgumentException("Persistent texture is not active on this device.", nameof(texture));
        }

        return descriptor;
    }

    internal BgfxBufferResource ResolveBuffer(RenderBufferHandle buffer)
    {
        if (m_activeGraph is null
            || GetHandleIdentity(buffer).generation != m_activeGraph.generation
            || !m_graphBuffers.TryGetValue(GetHandleIdentity(buffer).index, out BgfxBufferResource? resource))
        {
            throw new ArgumentException("Buffer is not active in the current BGFX graph.", nameof(buffer));
        }

        return resource;
    }

    internal BgfxBufferResource ResolveBuffer(PersistentBufferHandle buffer)
    {
        ValidatePersistentHandle(buffer);
        if (!m_persistentBuffers.TryGetValue(GetHandleIdentity(buffer).value, out BgfxBufferResource? resource))
        {
            throw new ArgumentException("Persistent buffer is not active on this device.", nameof(buffer));
        }

        return resource;
    }

    internal BgfxPipelineResource ResolvePipeline(GraphicsPipelineHandle pipeline)
    {
        ValidatePersistentHandle(pipeline);
        if (!m_graphicsPipelines.TryGetValue(GetHandleIdentity(pipeline).value, out BgfxPipelineResource? resource))
        {
            throw new ArgumentException("Graphics pipeline is not active on this device.", nameof(pipeline));
        }

        return resource;
    }

    internal BgfxPipelineResource ResolvePipeline(ComputePipelineHandle pipeline)
    {
        ValidatePersistentHandle(pipeline);
        if (!m_computePipelines.TryGetValue(GetHandleIdentity(pipeline).value, out BgfxPipelineResource? resource))
        {
            throw new ArgumentException("Compute pipeline is not active on this device.", nameof(pipeline));
        }

        return resource;
    }

    private void PrepareGraphBuffers(CompiledRenderGraph graph)
    {
        m_graphBuffers.Clear();
        m_transientBufferSlots.Clear();
        foreach (CompiledRenderBuffer buffer in graph.buffers)
        {
            BgfxBufferResource resource;
            if (buffer.imported)
            {
                resource = ResolveBuffer(buffer.persistentHandle);
                if (!resource.descriptor.Equals(buffer.descriptor))
                {
                    throw new InvalidOperationException(
                        $"Imported buffer '{buffer.name}' does not match its persistent descriptor.");
                }
            }
            else
            {
                if (buffer.physicalSlot < 0)
                {
                    continue;
                }

                if (!m_transientBufferSlots.TryGetValue(buffer.physicalSlot, out resource!))
                {
                    resource = CreateTransientBuffer(buffer.descriptor);
                    m_transientBufferSlots.Add(buffer.physicalSlot, resource);
                }
            }

            m_graphBuffers.Add(GetHandleIdentity(buffer.handle).index, resource);
        }
    }

    private BgfxBufferResource CreateNativeBuffer(
        PersistentBufferDescriptor descriptor,
        ReadOnlySpan<byte> initialData,
        string name)
    {
        RenderBufferDescriptor buffer = descriptor.buffer;
        if ((buffer.usage & RenderBufferUsage.Indirect) != 0
            && (buffer.usage & (RenderBufferUsage.Vertex | RenderBufferUsage.Index)) == 0)
        {
            if (!capabilities.Supports(GraphicsFeature.Indirect))
                throw new NotSupportedException("The active backend does not support indirect command buffers.");
            if (!initialData.IsEmpty)
            {
                throw new ArgumentException(
                    "BGFX indirect buffers cannot be initialized from CPU data.",
                    nameof(initialData));
            }
            bgfx.IndirectBufferHandle indirect = bgfx.create_indirect_buffer(
                checked((uint)buffer.elementCount));
            EnsureValid(indirect.Valid, name);
            return BgfxBufferResource.FromIndirect(buffer, indirect);
        }
        bool indexBuffer = (buffer.usage & RenderBufferUsage.Index) != 0;
        bool dynamic = (buffer.usage & (RenderBufferUsage.Dynamic | RenderBufferUsage.Storage)) != 0;
        if (indexBuffer
            && descriptor.indexFormat == RenderIndexFormat.UInt32
            && !capabilities.Supports(GraphicsFeature.Index32))
        {
            throw new NotSupportedException("The active backend does not support unsigned 32-bit indices.");
        }
        ValidateVertexLayoutCapabilities(descriptor.vertexLayout);
        ushort flags = BufferFlags(buffer, descriptor.indexFormat);

        if (indexBuffer)
        {
            if (initialData.IsEmpty && !dynamic)
            {
                throw new ArgumentException("An immutable index buffer requires complete initial data.", nameof(initialData));
            }

            if (dynamic)
            {
                bgfx.DynamicIndexBufferHandle handle = bgfx.create_dynamic_index_buffer(
                    checked((uint)buffer.elementCount),
                    flags);
                EnsureValid(handle.Valid, name);
                if (!initialData.IsEmpty)
                {
                    bgfx.update_dynamic_index_buffer(handle, 0, Copy(initialData));
                }
                return BgfxBufferResource.FromDynamicIndex(buffer, descriptor.indexFormat, handle);
            }

            bgfx.Memory* memory = Copy(initialData);
            bgfx.IndexBufferHandle staticHandle = bgfx.create_index_buffer(memory, flags);
            EnsureValid(staticHandle.Valid, name);
            bgfx.set_index_buffer_name(staticHandle, name, Utf8Length(name));
            return BgfxBufferResource.FromIndex(buffer, descriptor.indexFormat, staticHandle);
        }

        bool vertexBuffer = (buffer.usage & RenderBufferUsage.Vertex) != 0;
        bool storageBuffer = (buffer.usage & RenderBufferUsage.Storage) != 0;
        if (!vertexBuffer && !storageBuffer)
        {
            throw new NotSupportedException(
                "BGFX persistent buffers must be vertex, index, or storage resources.");
        }

        bgfx.VertexLayout nativeLayout = descriptor.vertexLayout is null
            ? CreateSkippedLayout(buffer.elementStride)
            : CreateNativeLayout(descriptor.vertexLayout);
        if (initialData.IsEmpty || dynamic)
        {
            bgfx.DynamicVertexBufferHandle handle = bgfx.create_dynamic_vertex_buffer(
                checked((uint)buffer.elementCount),
                &nativeLayout,
                flags);
            EnsureValid(handle.Valid, name);
            if (!initialData.IsEmpty)
            {
                bgfx.update_dynamic_vertex_buffer(handle, 0, Copy(initialData));
            }
            return BgfxBufferResource.FromDynamicVertex(buffer, descriptor.vertexLayout, handle);
        }

        bgfx.Memory* vertexMemory = Copy(initialData);
        bgfx.VertexBufferHandle vertexHandle = bgfx.create_vertex_buffer(vertexMemory, &nativeLayout, flags);
        EnsureValid(vertexHandle.Valid, name);
        bgfx.set_vertex_buffer_name(vertexHandle, name, Utf8Length(name));
        return BgfxBufferResource.FromVertex(buffer, descriptor.vertexLayout, vertexHandle);
    }

    private BgfxBufferResource CreateTransientBuffer(RenderBufferDescriptor descriptor)
    {
        if ((descriptor.usage & RenderBufferUsage.Indirect) != 0
            && (descriptor.usage & (RenderBufferUsage.Vertex | RenderBufferUsage.Index)) == 0)
        {
            if (!capabilities.Supports(GraphicsFeature.Indirect))
                throw new NotSupportedException("The active backend does not support indirect command buffers.");
            bgfx.IndirectBufferHandle indirect = bgfx.create_indirect_buffer(
                checked((uint)descriptor.elementCount));
            EnsureValid(indirect.Valid, "transient indirect buffer");
            return BgfxBufferResource.FromIndirect(descriptor, indirect);
        }
        if ((descriptor.usage & RenderBufferUsage.Index) != 0)
        {
            RenderIndexFormat format = descriptor.elementStride switch
            {
                2 => RenderIndexFormat.UInt16,
                4 => RenderIndexFormat.UInt32,
                _ => throw new NotSupportedException("Transient index buffers require a two- or four-byte stride.")
            };
            if (format == RenderIndexFormat.UInt32
                && !capabilities.Supports(GraphicsFeature.Index32))
            {
                throw new NotSupportedException("The active backend does not support unsigned 32-bit indices.");
            }
            bgfx.DynamicIndexBufferHandle index = bgfx.create_dynamic_index_buffer(
                checked((uint)descriptor.elementCount),
                BufferFlags(descriptor, format));
            EnsureValid(index.Valid, "transient index buffer");
            return BgfxBufferResource.FromDynamicIndex(descriptor, format, index);
        }

        if ((descriptor.usage & (RenderBufferUsage.Vertex | RenderBufferUsage.Storage)) == 0)
        {
            throw new NotSupportedException(
                "BGFX transient buffers must be vertex, index, or storage resources.");
        }

        bgfx.VertexLayout layout = CreateSkippedLayout(descriptor.elementStride);
        bgfx.DynamicVertexBufferHandle vertex = bgfx.create_dynamic_vertex_buffer(
            checked((uint)descriptor.elementCount),
            &layout,
            BufferFlags(descriptor, RenderIndexFormat.UInt32));
        EnsureValid(vertex.Valid, "transient vertex or storage buffer");
        return BgfxBufferResource.FromDynamicVertex(descriptor, null, vertex);
    }

    private BgfxPipelineResource CreateGraphicsPipelineResource(
        GraphicsPipelineDescriptor descriptor,
        string name)
    {
        ValidateBindingSlots(descriptor.bindings, allowStorageResources: false);
        ValidateVertexLayoutCapabilities(descriptor.vertexLayout);
        ValidateRasterStateCapabilities(descriptor.rasterState);
        bgfx.ShaderHandle vertexShader = InvalidShader();
        bgfx.ShaderHandle fragmentShader = InvalidShader();
        bgfx.ProgramHandle program = InvalidProgram();
        bgfx.VertexLayoutHandle layoutHandle = InvalidVertexLayout();
        try
        {
            vertexShader = CreateShader(descriptor.vertexShader.Span, $"{name}.Vertex");
            fragmentShader = CreateShader(descriptor.fragmentShader.Span, $"{name}.Fragment");
            Dictionary<string, ReflectedUniform> reflected = ReflectShaders(vertexShader, fragmentShader);
            IReadOnlyDictionary<string, BgfxShaderBindingResource> bindings =
                ValidateReflectedBindings(descriptor.bindings, reflected);

            program = bgfx.create_program(vertexShader, fragmentShader, false);
            EnsureValid(program.Valid, name);
            bgfx.destroy_shader(vertexShader);
            vertexShader = InvalidShader();
            bgfx.destroy_shader(fragmentShader);
            fragmentShader = InvalidShader();
            if (descriptor.vertexLayout is not null)
            {
                bgfx.VertexLayout nativeLayout = CreateNativeLayout(descriptor.vertexLayout);
                layoutHandle = bgfx.create_vertex_layout(&nativeLayout);
                EnsureValid(layoutHandle.Valid, $"{name} vertex layout");
            }
            return new BgfxPipelineResource(
                program,
                bindings,
                descriptor.vertexLayout,
                layoutHandle,
                descriptor.rasterState,
                false);
        }
        catch
        {
            if (program.Valid)
            {
                bgfx.destroy_program(program);
            }

            DestroyShaderIfValid(vertexShader);
            DestroyShaderIfValid(fragmentShader);

            if (layoutHandle.Valid)
            {
                bgfx.destroy_vertex_layout(layoutHandle);
            }

            throw;
        }
    }

    private void ValidateVertexLayoutCapabilities(RenderVertexLayout? layout)
    {
        if (layout is null)
            return;
        foreach (RenderVertexAttribute attribute in layout.attributes)
        {
            if (attribute.format is RenderVertexFormat.Half2 or RenderVertexFormat.Half4
                && !capabilities.Supports(GraphicsFeature.VertexAttributeHalf))
            {
                throw new NotSupportedException(
                    "The active backend does not support half-precision vertex attributes.");
            }

            if (attribute.format == RenderVertexFormat.UInt10Normalized4
                && !capabilities.Supports(GraphicsFeature.VertexAttributeUInt10))
            {
                throw new NotSupportedException(
                    "The active backend does not support packed 10:10:10:2 vertex attributes.");
            }
        }
    }

    private void ValidateRasterStateCapabilities(RenderRasterState state)
    {
        if (state.blend.alphaToCoverage
            && !capabilities.Supports(GraphicsFeature.AlphaToCoverage))
        {
            throw new NotSupportedException(
                "The active backend does not support alpha-to-coverage rasterization.");
        }
    }

    private BgfxPipelineResource CreateComputePipelineResource(
        ComputePipelineDescriptor descriptor,
        string name)
    {
        ValidateBindingSlots(descriptor.bindings, allowStorageResources: true);
        bgfx.ShaderHandle computeShader = InvalidShader();
        bgfx.ProgramHandle program = InvalidProgram();
        try
        {
            computeShader = CreateShader(descriptor.computeShader.Span, $"{name}.Compute");
            Dictionary<string, ReflectedUniform> reflected = ReflectShaders(computeShader);
            IReadOnlyDictionary<string, BgfxShaderBindingResource> bindings =
                ValidateReflectedBindings(descriptor.bindings, reflected);
            program = bgfx.create_compute_program(computeShader, false);
            EnsureValid(program.Valid, name);
            bgfx.destroy_shader(computeShader);
            computeShader = InvalidShader();
            return new BgfxPipelineResource(
                program,
                bindings,
                null,
                InvalidVertexLayout(),
                null,
                true);
        }
        catch
        {
            if (program.Valid)
            {
                bgfx.destroy_program(program);
            }

            DestroyShaderIfValid(computeShader);

            throw;
        }
    }

    private static IReadOnlyDictionary<string, BgfxShaderBindingResource> ValidateReflectedBindings(
        IReadOnlyList<RenderShaderBindingDescriptor> declaredBindings,
        IReadOnlyDictionary<string, ReflectedUniform> reflected)
    {
        Dictionary<string, RenderShaderBindingDescriptor> declared = declaredBindings
            .ToDictionary(static value => value.id.value, StringComparer.Ordinal);
        foreach ((string name, ReflectedUniform uniform) in reflected)
        {
            if (!declared.TryGetValue(name, out RenderShaderBindingDescriptor? binding)
                || binding.kind is RenderShaderBindingKind.StorageBuffer or RenderShaderBindingKind.StorageTexture)
            {
                throw new InvalidOperationException(
                    $"Shader reflection contains undeclared uniform '{name}'.");
            }

            bgfx.UniformType expectedType = binding.kind == RenderShaderBindingKind.Texture
                ? bgfx.UniformType.Sampler
                : ToNativeUniformType(binding.uniformType);
            if (uniform.type != expectedType || uniform.count != binding.count)
            {
                throw new InvalidOperationException(
                    $"Shader binding '{name}' reflected as {uniform.type}[{uniform.count}] but the manifest requires "
                    + $"{expectedType}[{binding.count}].");
            }
        }

        Dictionary<string, BgfxShaderBindingResource> result = new(StringComparer.Ordinal);
        foreach (RenderShaderBindingDescriptor binding in declaredBindings)
        {
            if (binding.kind is RenderShaderBindingKind.StorageBuffer or RenderShaderBindingKind.StorageTexture)
            {
                result.Add(binding.id.value, new BgfxShaderBindingResource(binding, InvalidUniform()));
                continue;
            }

            if (!reflected.TryGetValue(binding.id.value, out ReflectedUniform uniform))
            {
                throw new InvalidOperationException(
                    $"Shader manifest binding '{binding.id.value}' is absent from compiled reflection.");
            }

            result.Add(binding.id.value, new BgfxShaderBindingResource(binding, uniform.handle));
        }

        return result;
    }

    private static Dictionary<string, ReflectedUniform> ReflectShaders(params bgfx.ShaderHandle[] shaders)
    {
        Dictionary<string, ReflectedUniform> result = new(StringComparer.Ordinal);
        foreach (bgfx.ShaderHandle shader in shaders)
        {
            ushort count = bgfx.get_shader_uniforms(shader, null, 0);
            if (count == 0)
            {
                continue;
            }

            bgfx.UniformHandle[] uniforms = new bgfx.UniformHandle[count];
            fixed (bgfx.UniformHandle* handles = uniforms)
            {
                ushort written = bgfx.get_shader_uniforms(shader, handles, count);
                for (int index = 0; index < written; index++)
                {
                    bgfx.UniformInfo info = default;
                    bgfx.get_uniform_info(uniforms[index], &info);
                    string name = UniformName(info);
                    ReflectedUniform reflected = new(uniforms[index], info.type, info.num);
                    if (result.TryGetValue(name, out ReflectedUniform current)
                        && (current.type != reflected.type || current.count != reflected.count))
                    {
                        throw new InvalidOperationException(
                            $"Shader stages disagree about reflected binding '{name}'.");
                    }

                    result[name] = reflected;
                }
            }
        }

        return result;
    }

    private static string UniformName(bgfx.UniformInfo info)
    {
        byte* name = info.name;
        int length = 0;
        while (length < 256 && name[length] != 0)
        {
            length++;
        }

        return Encoding.UTF8.GetString(name, length);
    }

    private bgfx.ShaderHandle CreateShader(ReadOnlySpan<byte> binary, string name)
    {
        bgfx.Memory* memory = Copy(binary);
        bgfx.ShaderHandle shader = bgfx.create_shader(memory);
        EnsureValid(shader.Valid, name);
        bgfx.set_shader_name(shader, name, Utf8Length(name));
        return shader;
    }

    private bgfx.VertexLayout CreateNativeLayout(RenderVertexLayout layout)
    {
        bgfx.VertexLayout native = default;
        bgfx.vertex_layout_begin(&native, BgfxCapabilityMapper.ToNativeRenderer(capabilities.backend));
        int currentOffset = 0;
        foreach (RenderVertexAttribute attribute in layout.attributes)
        {
            AddVertexLayoutPadding(&native, attribute.byteOffset - currentOffset);
            (byte count, bgfx.AttribType type, bool normalized, bool asInteger) = AttributeFormat(attribute.format);
            bgfx.vertex_layout_add(
                &native,
                ToNativeAttribute(attribute.semantic),
                count,
                type,
                normalized,
                asInteger);
            currentOffset = checked(attribute.byteOffset + attribute.byteSize);
        }

        AddVertexLayoutPadding(&native, layout.stride - currentOffset);

        bgfx.vertex_layout_end(&native);
        if (native.stride != layout.stride)
        {
            throw new InvalidOperationException(
                $"BGFX produced vertex stride {native.stride}, expected {layout.stride}.");
        }

        return native;
    }

    private static void AddVertexLayoutPadding(bgfx.VertexLayout* layout, int byteCount)
    {
        if (byteCount < 0)
            throw new InvalidOperationException("A resolved vertex layout cannot contain overlapping attributes.");
        while (byteCount != 0)
        {
            byte chunk = checked((byte)Math.Min(byteCount, byte.MaxValue));
            bgfx.vertex_layout_skip(layout, chunk);
            byteCount -= chunk;
        }
    }

    private bgfx.VertexLayout CreateSkippedLayout(int stride)
    {
        bgfx.VertexLayout native = default;
        bgfx.vertex_layout_begin(&native, BgfxCapabilityMapper.ToNativeRenderer(capabilities.backend));
        int remaining = stride;
        while (remaining != 0)
        {
            byte chunk = checked((byte)Math.Min(remaining, byte.MaxValue));
            bgfx.vertex_layout_skip(&native, chunk);
            remaining -= chunk;
        }

        bgfx.vertex_layout_end(&native);
        return native;
    }

    private static (byte count, bgfx.AttribType type, bool normalized, bool asInteger) AttributeFormat(
        RenderVertexFormat format)
        => format switch
        {
            RenderVertexFormat.Float1 => (1, bgfx.AttribType.Float, false, false),
            RenderVertexFormat.Float2 => (2, bgfx.AttribType.Float, false, false),
            RenderVertexFormat.Float3 => (3, bgfx.AttribType.Float, false, false),
            RenderVertexFormat.Float4 => (4, bgfx.AttribType.Float, false, false),
            RenderVertexFormat.Half2 => (2, bgfx.AttribType.Half, false, false),
            RenderVertexFormat.Half4 => (4, bgfx.AttribType.Half, false, false),
            RenderVertexFormat.UInt8Normalized2 => (2, bgfx.AttribType.Uint8, true, false),
            RenderVertexFormat.UInt8Normalized4 => (4, bgfx.AttribType.Uint8, true, false),
            RenderVertexFormat.UInt8Integer2 => (2, bgfx.AttribType.Uint8, false, true),
            RenderVertexFormat.UInt8Integer4 => (4, bgfx.AttribType.Uint8, false, true),
            RenderVertexFormat.UInt10Normalized4 => (4, bgfx.AttribType.Uint10, true, false),
            RenderVertexFormat.Int16Normalized2 => (2, bgfx.AttribType.Int16, true, false),
            RenderVertexFormat.Int16Normalized4 => (4, bgfx.AttribType.Int16, true, false),
            RenderVertexFormat.Int16Integer2 => (2, bgfx.AttribType.Int16, false, true),
            RenderVertexFormat.Int16Integer4 => (4, bgfx.AttribType.Int16, false, true),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    private static bgfx.Attrib ToNativeAttribute(RenderVertexSemantic semantic)
        => semantic switch
        {
            RenderVertexSemantic.Position => bgfx.Attrib.Position,
            RenderVertexSemantic.Normal => bgfx.Attrib.Normal,
            RenderVertexSemantic.Tangent => bgfx.Attrib.Tangent,
            RenderVertexSemantic.Bitangent => bgfx.Attrib.Bitangent,
            RenderVertexSemantic.Color0 => bgfx.Attrib.Color0,
            RenderVertexSemantic.Color1 => bgfx.Attrib.Color1,
            RenderVertexSemantic.Color2 => bgfx.Attrib.Color2,
            RenderVertexSemantic.Color3 => bgfx.Attrib.Color3,
            RenderVertexSemantic.TextureCoordinate0 => bgfx.Attrib.TexCoord0,
            RenderVertexSemantic.TextureCoordinate1 => bgfx.Attrib.TexCoord1,
            RenderVertexSemantic.TextureCoordinate2 => bgfx.Attrib.TexCoord2,
            RenderVertexSemantic.TextureCoordinate3 => bgfx.Attrib.TexCoord3,
            RenderVertexSemantic.TextureCoordinate4 => bgfx.Attrib.TexCoord4,
            RenderVertexSemantic.TextureCoordinate5 => bgfx.Attrib.TexCoord5,
            RenderVertexSemantic.TextureCoordinate6 => bgfx.Attrib.TexCoord6,
            RenderVertexSemantic.TextureCoordinate7 => bgfx.Attrib.TexCoord7,
            RenderVertexSemantic.BlendIndices => bgfx.Attrib.Indices,
            RenderVertexSemantic.BlendWeights => bgfx.Attrib.Weight,
            _ => throw new ArgumentOutOfRangeException(nameof(semantic))
        };

    private static bgfx.UniformType ToNativeUniformType(RenderUniformType type)
        => type switch
        {
            RenderUniformType.Vector4 => bgfx.UniformType.Vec4,
            RenderUniformType.Matrix3x3 => bgfx.UniformType.Mat3,
            RenderUniformType.Matrix4x4 => bgfx.UniformType.Mat4,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

    private void ValidateBindingSlots(
        IReadOnlyList<RenderShaderBindingDescriptor> bindings,
        bool allowStorageResources)
    {
        var storageSlots = new HashSet<int>();
        foreach (RenderShaderBindingDescriptor binding in bindings)
        {
            if (binding.kind is RenderShaderBindingKind.Texture
                    or RenderShaderBindingKind.StorageTexture
                    or RenderShaderBindingKind.StorageBuffer
                && binding.slot > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bindings),
                    $"Binding '{binding.id.value}' slot exceeds the BGFX byte-sized stage range.");
            }
            if (binding.kind is not (RenderShaderBindingKind.StorageTexture or RenderShaderBindingKind.StorageBuffer))
                continue;
            if (!allowStorageResources)
            {
                throw new NotSupportedException(
                    $"BGFX graphics pipelines cannot bind storage resource '{binding.id.value}'.");
            }
            if (!storageSlots.Add(binding.slot))
            {
                throw new ArgumentException(
                    $"Storage binding slot {binding.slot} is declared more than once.",
                    nameof(bindings));
            }
            GraphicsFeature required = binding.kind == RenderShaderBindingKind.StorageTexture
                ? GraphicsFeature.StorageTexture
                : GraphicsFeature.StorageBuffer;
            if (!capabilities.Supports(required))
            {
                throw new NotSupportedException(
                    $"The active backend does not support {binding.kind} binding '{binding.id.value}'.");
            }
        }

        int storageCount = storageSlots.Count;
        if (storageCount > capabilities.limits.maxComputeBindings)
        {
            throw new NotSupportedException(
                $"Pipeline requires {storageCount} storage bindings, but the device supports "
                + $"{capabilities.limits.maxComputeBindings}.");
        }
    }

    private static ushort BufferFlags(RenderBufferDescriptor descriptor, RenderIndexFormat indexFormat)
    {
        bgfx.BufferFlags flags = bgfx.BufferFlags.None;
        if ((descriptor.usage & RenderBufferUsage.Storage) != 0)
        {
            flags |= bgfx.BufferFlags.ComputeReadWrite;
        }

        if ((descriptor.usage & RenderBufferUsage.Indirect) != 0)
        {
            flags |= bgfx.BufferFlags.DrawIndirect;
        }

        if ((descriptor.usage & RenderBufferUsage.Index) != 0 && indexFormat == RenderIndexFormat.UInt32)
        {
            flags |= bgfx.BufferFlags.Index32;
        }

        return (ushort)flags;
    }

    private static bgfx.Memory* Copy(ReadOnlySpan<byte> data)
    {
        fixed (byte* pointer = data)
        {
            return bgfx.copy(pointer, checked((uint)data.Length));
        }
    }

    private static void EnsureValid(bool valid, string name)
    {
        if (!valid)
        {
            throw new InvalidOperationException($"BGFX could not create '{name}'.");
        }
    }

    private static void DestroyShaderIfValid(bgfx.ShaderHandle shader)
    {
        if (shader.Valid)
        {
            bgfx.destroy_shader(shader);
        }
    }

    private static void DestroyBufferImmediately(BgfxBufferResource buffer)
    {
        switch (buffer.kind)
        {
            case BgfxBufferKind.Vertex:
                bgfx.destroy_vertex_buffer(new bgfx.VertexBufferHandle { idx = buffer.nativeIndex });
                break;
            case BgfxBufferKind.Index:
                bgfx.destroy_index_buffer(new bgfx.IndexBufferHandle { idx = buffer.nativeIndex });
                break;
            case BgfxBufferKind.DynamicVertex:
                bgfx.destroy_dynamic_vertex_buffer(new bgfx.DynamicVertexBufferHandle { idx = buffer.nativeIndex });
                break;
            case BgfxBufferKind.DynamicIndex:
                bgfx.destroy_dynamic_index_buffer(new bgfx.DynamicIndexBufferHandle { idx = buffer.nativeIndex });
                break;
            case BgfxBufferKind.Indirect:
                bgfx.destroy_indirect_buffer(new bgfx.IndirectBufferHandle { idx = buffer.nativeIndex });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(buffer));
        }
    }

    private static void DestroyPipelineImmediately(BgfxPipelineResource pipeline)
    {
        bgfx.destroy_program(pipeline.program);
        if (pipeline.vertexLayoutHandle.Valid)
        {
            bgfx.destroy_vertex_layout(pipeline.vertexLayoutHandle);
        }
    }

    private void ValidatePersistentHandle(PersistentBufferHandle buffer)
    {
        if (!buffer.isValid || GetHandleIdentity(buffer).generation != generation)
        {
            throw new ArgumentException("Buffer handle belongs to another device generation.", nameof(buffer));
        }
    }

    private void ValidatePersistentHandle(GraphicsPipelineHandle pipeline)
    {
        if (!pipeline.isValid || GetHandleIdentity(pipeline).generation != generation)
        {
            throw new ArgumentException("Graphics pipeline belongs to another device generation.", nameof(pipeline));
        }
    }

    private void ValidatePersistentHandle(ComputePipelineHandle pipeline)
    {
        if (!pipeline.isValid || GetHandleIdentity(pipeline).generation != generation)
        {
            throw new ArgumentException("Compute pipeline belongs to another device generation.", nameof(pipeline));
        }
    }

    private static bgfx.ShaderHandle InvalidShader()
        => new() { idx = ushort.MaxValue };

    private static bgfx.ProgramHandle InvalidProgram()
        => new() { idx = ushort.MaxValue };

    private static bgfx.UniformHandle InvalidUniform()
        => new() { idx = ushort.MaxValue };

    private static bgfx.VertexLayoutHandle InvalidVertexLayout()
        => new() { idx = ushort.MaxValue };

    private readonly record struct ReflectedUniform(
        bgfx.UniformHandle handle,
        bgfx.UniformType type,
        ushort count);
}
