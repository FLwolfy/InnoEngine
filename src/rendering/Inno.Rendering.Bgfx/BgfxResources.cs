using System;
using System.Collections.Generic;
using Inno.Native.Bgfx;
using Inno.Rendering;

namespace Inno.Rendering.Bgfx;

internal enum BgfxBufferKind
{
    Vertex,
    Index,
    DynamicVertex,
    DynamicIndex,
    Indirect
}

internal sealed class BgfxBufferResource
{
    private BgfxBufferResource(
        RenderBufferDescriptor descriptor,
        RenderVertexLayout? vertexLayout,
        RenderIndexFormat indexFormat,
        BgfxBufferKind kind,
        ushort nativeIndex)
    {
        this.descriptor = descriptor;
        this.vertexLayout = vertexLayout;
        this.indexFormat = indexFormat;
        this.kind = kind;
        this.nativeIndex = nativeIndex;
    }

    /// <summary>
    /// Gets the backend-neutral descriptor used to validate and recreate this resource.
    /// </summary>
    public RenderBufferDescriptor descriptor { get; }
    /// <summary>
    /// Gets the optional vertex layout required by this resource or pipeline.
    /// </summary>
    public RenderVertexLayout? vertexLayout { get; }
    /// <summary>
    /// Gets the element width used by this index buffer.
    /// </summary>
    public RenderIndexFormat indexFormat { get; }
    /// <summary>
    /// Gets the operation kind that determines how this value is interpreted.
    /// </summary>
    public BgfxBufferKind kind { get; }
    /// <summary>
    /// Gets the BGFX dynamic-buffer index used by deferred destruction.
    /// </summary>
    public ushort nativeIndex { get; }

    /// <summary>
    /// Creates the target representation from the supplied vertex value.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor consumed by from vertex; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="layout">
    /// The layout consumed by from vertex; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="handle">
    /// The opaque handle validated by this operation.
    /// </param>
    /// <returns>
    /// The validated bgfx buffer resource that represents the completed operation.
    /// </returns>
    public static BgfxBufferResource FromVertex(
        RenderBufferDescriptor descriptor,
        RenderVertexLayout? layout,
        bgfx.VertexBufferHandle handle)
        => new(descriptor, layout, RenderIndexFormat.UInt32, BgfxBufferKind.Vertex, handle.idx);

    /// <summary>
    /// Creates the target representation from the supplied index value.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor consumed by from index; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="format">
    /// The format consumed by from index; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="handle">
    /// The opaque handle validated by this operation.
    /// </param>
    /// <returns>
    /// The validated bgfx buffer resource that represents the completed operation.
    /// </returns>
    public static BgfxBufferResource FromIndex(
        RenderBufferDescriptor descriptor,
        RenderIndexFormat format,
        bgfx.IndexBufferHandle handle)
        => new(descriptor, null, format, BgfxBufferKind.Index, handle.idx);

    /// <summary>
    /// Creates the target representation from the supplied dynamic vertex value.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor consumed by from dynamic vertex; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="layout">
    /// The layout consumed by from dynamic vertex; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="handle">
    /// The opaque handle validated by this operation.
    /// </param>
    /// <returns>
    /// The validated bgfx buffer resource that represents the completed operation.
    /// </returns>
    public static BgfxBufferResource FromDynamicVertex(
        RenderBufferDescriptor descriptor,
        RenderVertexLayout? layout,
        bgfx.DynamicVertexBufferHandle handle)
        => new(descriptor, layout, RenderIndexFormat.UInt32, BgfxBufferKind.DynamicVertex, handle.idx);

    /// <summary>
    /// Creates the target representation from the supplied dynamic index value.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor consumed by from dynamic index; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="format">
    /// The format consumed by from dynamic index; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="handle">
    /// The opaque handle validated by this operation.
    /// </param>
    /// <returns>
    /// The validated bgfx buffer resource that represents the completed operation.
    /// </returns>
    public static BgfxBufferResource FromDynamicIndex(
        RenderBufferDescriptor descriptor,
        RenderIndexFormat format,
        bgfx.DynamicIndexBufferHandle handle)
        => new(descriptor, null, format, BgfxBufferKind.DynamicIndex, handle.idx);

    /// <summary>
    /// Creates the target representation from the supplied indirect value.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor consumed by from indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="handle">
    /// The opaque handle validated by this operation.
    /// </param>
    /// <returns>
    /// The validated bgfx buffer resource that represents the completed operation.
    /// </returns>
    public static BgfxBufferResource FromIndirect(
        RenderBufferDescriptor descriptor,
        bgfx.IndirectBufferHandle handle)
        => new(descriptor, null, RenderIndexFormat.UInt32, BgfxBufferKind.Indirect, handle.idx);
}

internal sealed class BgfxShaderBindingResource
{
    /// <summary>
    /// Creates a validated bgfx shader binding resource instance.
    /// </summary>
    /// <param name="descriptor">
    /// The descriptor consumed by bgfx shader binding resource; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="uniform">
    /// The uniform consumed by bgfx shader binding resource; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public BgfxShaderBindingResource(
        RenderShaderBindingDescriptor descriptor,
        bgfx.UniformHandle uniform)
    {
        this.descriptor = descriptor;
        this.uniform = uniform;
    }

    /// <summary>
    /// Gets the backend-neutral descriptor used to validate and recreate this resource.
    /// </summary>
    public RenderShaderBindingDescriptor descriptor { get; }
    /// <summary>
    /// Gets the BGFX uniform handle backing this shader binding.
    /// </summary>
    public bgfx.UniformHandle uniform { get; }
}

internal sealed class BgfxPipelineResource
{
    /// <summary>
    /// Creates a validated bgfx pipeline resource instance.
    /// </summary>
    /// <param name="program">
    /// The program consumed by bgfx pipeline resource; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="bindings">
    /// The bindings consumed by bgfx pipeline resource; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="vertexLayout">
    /// The vertex layout consumed by bgfx pipeline resource; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="vertexLayoutHandle">
    /// The vertex layout handle consumed by bgfx pipeline resource; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="rasterState">
    /// The raster state consumed by bgfx pipeline resource; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="compute">
    /// Whether compute behavior is enabled while bgfx pipeline resource executes.
    /// </param>
    public BgfxPipelineResource(
        bgfx.ProgramHandle program,
        IReadOnlyDictionary<string, BgfxShaderBindingResource> bindings,
        RenderVertexLayout? vertexLayout,
        bgfx.VertexLayoutHandle vertexLayoutHandle,
        RenderRasterState? rasterState,
        bool compute)
    {
        this.program = program;
        this.bindings = bindings;
        this.vertexLayout = vertexLayout;
        this.vertexLayoutHandle = vertexLayoutHandle;
        this.rasterState = rasterState;
        this.compute = compute;
    }

    /// <summary>
    /// Gets the linked BGFX shader program owned by this pipeline resource.
    /// </summary>
    public bgfx.ProgramHandle program { get; }
    /// <summary>
    /// Gets the immutable lookup of stable binding IDs to BGFX uniform resources.
    /// </summary>
    public IReadOnlyDictionary<string, BgfxShaderBindingResource> bindings { get; }
    /// <summary>
    /// Gets the optional vertex layout required by this resource or pipeline.
    /// </summary>
    public RenderVertexLayout? vertexLayout { get; }
    /// <summary>
    /// Gets the BGFX vertex-layout handle owned by this pipeline resource.
    /// </summary>
    public bgfx.VertexLayoutHandle vertexLayoutHandle { get; }
    /// <summary>
    /// Gets the optional rasterization state encoded by this pipeline.
    /// </summary>
    public RenderRasterState? rasterState { get; }
    /// <summary>
    /// Gets whether the caller-visible condition represented by this property is satisfied.
    /// </summary>
    public bool compute { get; }
}
