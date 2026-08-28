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

/// <summary>
/// Provides backend-neutral draw, dispatch and copy commands for one compiled pass.
/// </summary>
public abstract class RenderCommandEncoder
{
    /// <summary>Binds a graphics pipeline for subsequent draw commands.</summary>
    /// <param name="pipeline">Graphics pipeline to bind.</param>
    public abstract void BindGraphicsPipeline(GraphicsPipelineHandle pipeline);

    /// <summary>Binds a compute pipeline for subsequent dispatch commands.</summary>
    /// <param name="pipeline">Compute pipeline to bind.</param>
    public abstract void BindComputePipeline(ComputePipelineHandle pipeline);

    /// <summary>Binds a graph texture to a shader interface slot.</summary>
    /// <param name="binding">Stable shader binding identifier.</param>
    /// <param name="texture">Graph texture to bind.</param>
    public abstract void BindTexture(RenderBindingId binding, RenderTextureHandle texture);

    /// <summary>Binds a persistent texture to a shader interface slot.</summary>
    /// <param name="binding">Stable shader binding identifier.</param>
    /// <param name="texture">Persistent texture owned by the active device generation.</param>
    public abstract void BindTexture(RenderBindingId binding, PersistentTextureHandle texture);

    /// <summary>Binds a graph buffer to a shader interface slot.</summary>
    /// <param name="binding">Stable shader binding identifier.</param>
    /// <param name="buffer">Graph buffer to bind.</param>
    public abstract void BindBuffer(RenderBindingId binding, RenderBufferHandle buffer);

    /// <summary>Binds a persistent storage buffer to a shader interface slot.</summary>
    /// <param name="binding">Stable shader binding identifier.</param>
    /// <param name="buffer">Persistent buffer owned by the active device generation.</param>
    public abstract void BindBuffer(RenderBindingId binding, PersistentBufferHandle buffer);

    /// <summary>Uploads one uniform value using manifest-validated bytes.</summary>
    /// <param name="binding">Stable shader binding identifier.</param>
    /// <param name="value">Uniform bytes matching the reflected shader interface.</param>
    public abstract void SetUniform(RenderBindingId binding, ReadOnlySpan<byte> value);

    /// <summary>Sets the current object transform from one column-major 4x4 matrix.</summary>
    /// <param name="columnMajorMatrix">Exactly sixteen floating-point matrix values.</param>
    public abstract void SetTransform(ReadOnlySpan<float> columnMajorMatrix);

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

    /// <summary>Binds a graph buffer as index input.</summary>
    /// <param name="buffer">Index buffer.</param>
    /// <param name="firstIndex">First index element.</param>
    public abstract void BindIndexBuffer(RenderBufferHandle buffer, int firstIndex = 0);

    /// <summary>Binds a persistent buffer as index input.</summary>
    /// <param name="buffer">Persistent index buffer.</param>
    /// <param name="firstIndex">First index element.</param>
    public abstract void BindIndexBuffer(PersistentBufferHandle buffer, int firstIndex = 0);

    /// <summary>Issues a non-indexed draw.</summary>
    /// <param name="vertexCount">Number of vertices.</param>
    /// <param name="instanceCount">Number of instances.</param>
    public abstract void Draw(int vertexCount, int instanceCount = 1);

    /// <summary>Issues an indexed draw.</summary>
    /// <param name="indexCount">Number of indices.</param>
    /// <param name="instanceCount">Number of instances.</param>
    public abstract void DrawIndexed(int indexCount, int instanceCount = 1);

    /// <summary>Dispatches compute workgroups.</summary>
    /// <param name="groupCountX">Workgroup count on X.</param>
    /// <param name="groupCountY">Workgroup count on Y.</param>
    /// <param name="groupCountZ">Workgroup count on Z.</param>
    public abstract void Dispatch(int groupCountX, int groupCountY = 1, int groupCountZ = 1);

    /// <summary>Copies all compatible subresources between graph textures.</summary>
    /// <param name="source">Copy source texture.</param>
    /// <param name="destination">Copy destination texture.</param>
    public abstract void CopyTexture(RenderTextureHandle source, RenderTextureHandle destination);
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
