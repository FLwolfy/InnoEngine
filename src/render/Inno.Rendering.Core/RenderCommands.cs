using System;

namespace Inno.Rendering.Core;

/// <summary>
/// Identifies a shader binding by stable manifest name.
/// </summary>
public readonly record struct RenderBindingId
{
    /// <summary>
    /// Creates a stable shader binding identifier.
    /// </summary>
    /// <param name="value">Stable manifest binding name.</param>
    public RenderBindingId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>Gets the stable manifest binding name.</summary>
    public string value { get; }

    /// <summary>Gets whether the identifier contains a stable manifest name.</summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);
}

/// <summary>
/// Identifies a backend-neutral graphics pipeline object.
/// </summary>
public readonly record struct GraphicsPipelineHandle
{
    internal GraphicsPipelineHandle(ulong value, uint deviceGeneration)
    {
        this.value = value;
        this.deviceGeneration = deviceGeneration;
    }

    internal ulong value { get; }
    internal uint deviceGeneration { get; }

    /// <summary>Gets whether the handle identifies a graphics pipeline.</summary>
    public bool isValid => value != 0 && deviceGeneration != 0;
}

/// <summary>
/// Identifies a backend-neutral compute pipeline object.
/// </summary>
public readonly record struct ComputePipelineHandle
{
    internal ComputePipelineHandle(ulong value, uint deviceGeneration)
    {
        this.value = value;
        this.deviceGeneration = deviceGeneration;
    }

    internal ulong value { get; }
    internal uint deviceGeneration { get; }

    /// <summary>Gets whether the handle identifies a compute pipeline.</summary>
    public bool isValid => value != 0 && deviceGeneration != 0;
}

/// <summary>Describes one texture subresource box for copy and blit commands.</summary>
public readonly record struct RenderTextureRegion
{
    /// <summary>Creates a texture subresource box.</summary>
    /// <param name="mip">Zero-based mip level.</param>
    /// <param name="x">Horizontal texel origin.</param>
    /// <param name="y">Vertical texel origin.</param>
    /// <param name="layer">Array layer or depth-slice origin.</param>
    /// <param name="width">Positive texel width.</param>
    /// <param name="height">Positive texel height.</param>
    /// <param name="depth">Positive layer or depth-slice count.</param>
    public RenderTextureRegion(int mip, int x, int y, int layer, int width, int height, int depth = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mip);
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegative(layer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        this.mip = mip;
        this.x = x;
        this.y = y;
        this.layer = layer;
        this.width = width;
        this.height = height;
        this.depth = depth;
    }

    /// <summary>Gets the zero-based mip level.</summary>
    public int mip { get; }

    /// <summary>Gets the horizontal texel origin.</summary>
    public int x { get; }

    /// <summary>Gets the vertical texel origin.</summary>
    public int y { get; }

    /// <summary>Gets the array layer or depth-slice origin.</summary>
    public int layer { get; }

    /// <summary>Gets the texel width.</summary>
    public int width { get; }

    /// <summary>Gets the texel height.</summary>
    public int height { get; }

    /// <summary>Gets the layer or depth-slice count.</summary>
    public int depth { get; }
}

/// <summary>
/// Provides backend-neutral draw, dispatch and copy commands for one compiled pass.
/// </summary>
public abstract class RenderCommandEncoder
{
    private ulong m_frameIndex;
    private bool m_hasFrameIndex;

    /// <summary>Binds a graphics pipeline for subsequent draw commands.</summary>
    /// <param name="pipeline">Graphics pipeline to bind.</param>
    public abstract void BindGraphicsPipeline(GraphicsPipelineHandle pipeline);

    /// <summary>Binds a compute pipeline for subsequent dispatch commands.</summary>
    /// <param name="pipeline">Compute pipeline to bind.</param>
    public abstract void BindComputePipeline(ComputePipelineHandle pipeline);

    /// <summary>Binds a graph texture to a shader interface slot.</summary>
    /// <param name="binding">Stable shader binding identifier.</param>
    /// <param name="texture">Graph texture to bind.</param>
    public void BindTexture(RenderBindingId binding, RenderTextureHandle texture)
        => BindTexture(binding, texture, RenderSamplerState.linearClamp);

    /// <summary>Binds a graph texture and explicit sampler state.</summary>
    /// <param name="binding">Stable shader binding identifier.</param>
    /// <param name="texture">Graph texture to bind.</param>
    /// <param name="sampler">Backend-neutral sample state.</param>
    public abstract void BindTexture(
        RenderBindingId binding,
        RenderTextureHandle texture,
        RenderSamplerState sampler);

    /// <summary>Binds a persistent texture to a shader interface slot.</summary>
    /// <param name="binding">Stable shader binding identifier.</param>
    /// <param name="texture">Persistent texture owned by the active device generation.</param>
    public void BindTexture(RenderBindingId binding, PersistentTextureHandle texture)
        => BindTexture(binding, texture, RenderSamplerState.linearClamp);

    /// <summary>Binds a persistent texture and explicit sampler state.</summary>
    /// <param name="binding">Stable shader binding identifier.</param>
    /// <param name="texture">Persistent texture owned by the active device generation.</param>
    /// <param name="sampler">Backend-neutral sample state.</param>
    public abstract void BindTexture(
        RenderBindingId binding,
        PersistentTextureHandle texture,
        RenderSamplerState sampler);

    /// <summary>Binds a graph texture for shader storage access.</summary>
    /// <param name="binding">Stable storage-image binding identifier.</param>
    /// <param name="texture">Graph texture created with storage usage.</param>
    /// <param name="mipLevel">Mip level exposed to the shader.</param>
    public abstract void BindStorageTexture(
        RenderBindingId binding,
        RenderTextureHandle texture,
        int mipLevel = 0);

    /// <summary>Binds a persistent texture for shader storage access.</summary>
    /// <param name="binding">Stable storage-image binding identifier.</param>
    /// <param name="texture">Persistent texture created with storage usage.</param>
    /// <param name="mipLevel">Mip level exposed to the shader.</param>
    public abstract void BindStorageTexture(
        RenderBindingId binding,
        PersistentTextureHandle texture,
        int mipLevel = 0);

    /// <summary>Binds a graph buffer to a shader interface slot.</summary>
    /// <param name="binding">Stable shader binding identifier.</param>
    /// <param name="buffer">Graph buffer to bind.</param>
    public abstract void BindBuffer(RenderBindingId binding, RenderBufferHandle buffer);

    /// <summary>Binds a persistent storage buffer to a shader interface slot.</summary>
    /// <param name="binding">Stable shader binding identifier.</param>
    /// <param name="buffer">Persistent buffer owned by the active device generation.</param>
    public abstract void BindBuffer(RenderBindingId binding, PersistentBufferHandle buffer);

    /// <summary>Binds a frame-uploaded storage buffer to a shader interface slot.</summary>
    /// <param name="binding">Stable shader binding identifier.</param>
    /// <param name="buffer">Current-frame storage slice.</param>
    public void BindBuffer(RenderBindingId binding, RenderBufferSlice buffer)
    {
        ValidateSlice(buffer, RenderBufferUsage.Storage);
        if (buffer.firstElement != 0)
        {
            throw new ArgumentException(
                "Storage upload slices must begin at the first backing element.",
                nameof(buffer));
        }
        BindBuffer(binding, buffer.buffer);
    }

    /// <summary>Uploads one uniform value using manifest-validated bytes.</summary>
    /// <param name="binding">Stable shader binding identifier.</param>
    /// <param name="value">Uniform bytes matching the reflected shader interface.</param>
    public abstract void SetUniform(RenderBindingId binding, ReadOnlySpan<byte> value);

    /// <summary>Sets the current object transform from one column-major 4x4 matrix.</summary>
    /// <param name="columnMajorMatrix">Exactly sixteen floating-point matrix values.</param>
    public abstract void SetTransform(ReadOnlySpan<float> columnMajorMatrix);

    /// <summary>Overrides raster state for subsequent draws in the current pass.</summary>
    /// <param name="state">Complete backend-neutral raster state.</param>
    public abstract void SetRasterState(RenderRasterState state);

    /// <summary>Sets two-sided stencil state for subsequent draws.</summary>
    /// <param name="state">Complete backend-neutral stencil state.</param>
    public abstract void SetStencil(RenderStencilState state);

    /// <summary>Changes the viewport rectangle for the current pass view.</summary>
    /// <param name="x">Horizontal framebuffer origin.</param>
    /// <param name="y">Vertical framebuffer origin.</param>
    /// <param name="width">Positive framebuffer width.</param>
    /// <param name="height">Positive framebuffer height.</param>
    public abstract void SetViewport(int x, int y, int width, int height);

    /// <summary>Restricts subsequent rasterization to a pixel rectangle in the active view.</summary>
    /// <param name="x">Left edge in framebuffer pixels.</param>
    /// <param name="y">Top edge in framebuffer pixels.</param>
    /// <param name="width">Positive rectangle width in pixels.</param>
    /// <param name="height">Positive rectangle height in pixels.</param>
    public abstract void SetScissor(int x, int y, int width, int height);

    /// <summary>Binds a graph buffer as vertex input.</summary>
    /// <param name="buffer">Vertex buffer.</param>
    /// <param name="firstVertex">First vertex element.</param>
    public abstract void BindVertexBuffer(RenderBufferHandle buffer, int firstVertex = 0);

    /// <summary>Binds a persistent buffer as vertex input.</summary>
    /// <param name="buffer">Persistent vertex buffer.</param>
    /// <param name="firstVertex">First vertex element.</param>
    public abstract void BindVertexBuffer(PersistentBufferHandle buffer, int firstVertex = 0);

    /// <summary>Binds a frame-uploaded vertex slice.</summary>
    /// <param name="buffer">Current-frame vertex slice.</param>
    public void BindVertexBuffer(RenderBufferSlice buffer)
    {
        ValidateSlice(buffer, RenderBufferUsage.Vertex);
        BindVertexBuffer(buffer.buffer, buffer.firstElement);
    }

    /// <summary>Binds a graph buffer as index input.</summary>
    /// <param name="buffer">Index buffer.</param>
    /// <param name="firstIndex">First index element.</param>
    public abstract void BindIndexBuffer(RenderBufferHandle buffer, int firstIndex = 0);

    /// <summary>Binds a persistent buffer as index input.</summary>
    /// <param name="buffer">Persistent index buffer.</param>
    /// <param name="firstIndex">First index element.</param>
    public abstract void BindIndexBuffer(PersistentBufferHandle buffer, int firstIndex = 0);

    /// <summary>Binds a frame-uploaded index slice.</summary>
    /// <param name="buffer">Current-frame index slice.</param>
    public void BindIndexBuffer(RenderBufferSlice buffer)
    {
        ValidateSlice(buffer, RenderBufferUsage.Index);
        BindIndexBuffer(buffer.buffer, buffer.firstElement);
    }

    /// <summary>Binds graph buffer elements as per-instance input.</summary>
    /// <param name="buffer">Vertex-compatible graph buffer containing instance records.</param>
    /// <param name="firstInstance">First instance element.</param>
    /// <param name="instanceCount">Positive bound instance count.</param>
    public abstract void BindInstanceBuffer(
        RenderBufferHandle buffer,
        int firstInstance,
        int instanceCount);

    /// <summary>Binds persistent buffer elements as per-instance input.</summary>
    /// <param name="buffer">Vertex-compatible persistent buffer containing instance records.</param>
    /// <param name="firstInstance">First instance element.</param>
    /// <param name="instanceCount">Positive bound instance count.</param>
    public abstract void BindInstanceBuffer(
        PersistentBufferHandle buffer,
        int firstInstance,
        int instanceCount);

    /// <summary>Binds a frame-uploaded vertex slice as per-instance input.</summary>
    /// <param name="buffer">Current-frame instance records.</param>
    public void BindInstanceBuffer(RenderBufferSlice buffer)
    {
        ValidateSlice(buffer, RenderBufferUsage.Vertex);
        BindInstanceBuffer(buffer.buffer, buffer.firstElement, buffer.elementCount);
    }

    /// <summary>Issues a non-indexed draw.</summary>
    /// <param name="vertexCount">Number of vertices.</param>
    /// <param name="instanceCount">Number of instances.</param>
    public abstract void Draw(int vertexCount, int instanceCount = 1);

    /// <summary>
    /// Issues a procedural non-indexed draw that does not consume a vertex buffer.
    /// </summary>
    /// <param name="vertexCount">Number of shader-generated vertices.</param>
    /// <param name="instanceCount">Number of instances.</param>
    public abstract void DrawProcedural(int vertexCount, int instanceCount = 1);

    /// <summary>Issues an indexed draw.</summary>
    /// <param name="indexCount">Number of indices.</param>
    /// <param name="instanceCount">Number of instances.</param>
    public abstract void DrawIndexed(int indexCount, int instanceCount = 1);

    /// <summary>Issues graphics commands stored in an indirect graph buffer.</summary>
    /// <param name="buffer">Indirect graph buffer.</param>
    /// <param name="firstCommand">First command record.</param>
    /// <param name="commandCount">Positive command count.</param>
    public abstract void DrawIndirect(
        RenderBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1);

    /// <summary>Issues graphics commands stored in an indirect persistent buffer.</summary>
    /// <param name="buffer">Indirect persistent buffer.</param>
    /// <param name="firstCommand">First command record.</param>
    /// <param name="commandCount">Positive command count.</param>
    public abstract void DrawIndirect(
        PersistentBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1);

    /// <summary>Dispatches compute workgroups.</summary>
    /// <param name="groupCountX">Workgroup count on X.</param>
    /// <param name="groupCountY">Workgroup count on Y.</param>
    /// <param name="groupCountZ">Workgroup count on Z.</param>
    public abstract void Dispatch(int groupCountX, int groupCountY = 1, int groupCountZ = 1);

    /// <summary>Dispatches compute commands stored in an indirect graph buffer.</summary>
    /// <param name="buffer">Indirect graph buffer.</param>
    /// <param name="firstCommand">First command record.</param>
    /// <param name="commandCount">Positive command count.</param>
    public abstract void DispatchIndirect(
        RenderBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1);

    /// <summary>Dispatches compute commands stored in an indirect persistent buffer.</summary>
    /// <param name="buffer">Indirect persistent buffer.</param>
    /// <param name="firstCommand">First command record.</param>
    /// <param name="commandCount">Positive command count.</param>
    public abstract void DispatchIndirect(
        PersistentBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1);

    /// <summary>Copies all compatible subresources between graph textures.</summary>
    /// <param name="source">Copy source texture.</param>
    /// <param name="destination">Copy destination texture.</param>
    public abstract void CopyTexture(RenderTextureHandle source, RenderTextureHandle destination);

    /// <summary>Blits compatible texture regions without CPU readback.</summary>
    /// <param name="source">Copy source texture.</param>
    /// <param name="sourceRegion">Source subresource box.</param>
    /// <param name="destination">Copy destination texture.</param>
    /// <param name="destinationRegion">Destination origin and compatible extent.</param>
    public abstract void BlitTexture(
        RenderTextureHandle source,
        RenderTextureRegion sourceRegion,
        RenderTextureHandle destination,
        RenderTextureRegion destinationRegion);

    /// <summary>Copies complete compatible graph buffer ranges.</summary>
    /// <param name="source">Copy source buffer.</param>
    /// <param name="destination">Copy destination buffer.</param>
    /// <exception cref="NotSupportedException">Thrown when the backend has no safe buffer-copy path.</exception>
    public abstract void CopyBuffer(RenderBufferHandle source, RenderBufferHandle destination);

    internal void SetFrameIndex(ulong frameIndex)
    {
        m_frameIndex = frameIndex;
        m_hasFrameIndex = true;
    }

    private void ValidateSlice(RenderBufferSlice buffer, RenderBufferUsage requiredUsage)
    {
        if (!buffer.isValid)
            throw new ArgumentException("A frame buffer slice must be valid.", nameof(buffer));
        if (!m_hasFrameIndex || buffer.frameIndex != m_frameIndex)
        {
            throw new ArgumentException(
                "A frame buffer slice cannot be used outside the frame that uploaded it.",
                nameof(buffer));
        }
        if ((buffer.usage & requiredUsage) == 0)
        {
            throw new ArgumentException(
                $"The frame buffer slice does not declare {requiredUsage} usage.",
                nameof(buffer));
        }
    }
}

/// <summary>
/// Supplies the command encoder and immutable frame identity to a pass callback.
/// </summary>
public sealed class RenderPassContext
{
    internal RenderPassContext(RenderCommandEncoder commands, ulong frameIndex)
    {
        this.commands = commands;
        this.frameIndex = frameIndex;
    }

    /// <summary>Gets the command encoder scoped to the current pass.</summary>
    public RenderCommandEncoder commands { get; }

    /// <summary>Gets the monotonic frame index.</summary>
    public ulong frameIndex { get; }
}

/// <summary>
/// Executes one frame-scoped pass payload.
/// </summary>
/// <typeparam name="TPassData">Neutral pass payload type.</typeparam>
/// <param name="passData">Pass payload captured for this frame only.</param>
/// <param name="context">Current pass execution context.</param>
public delegate void RenderPassExecute<in TPassData>(TPassData passData, RenderPassContext context);
