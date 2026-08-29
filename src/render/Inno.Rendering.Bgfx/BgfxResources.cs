using System;
using System.Collections.Generic;
using Inno.Native.Bgfx;
using Inno.Rendering.Core;

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

    public RenderBufferDescriptor descriptor { get; }
    public RenderVertexLayout? vertexLayout { get; }
    public RenderIndexFormat indexFormat { get; }
    public BgfxBufferKind kind { get; }
    public ushort nativeIndex { get; }

    public static BgfxBufferResource FromVertex(
        RenderBufferDescriptor descriptor,
        RenderVertexLayout? layout,
        bgfx.VertexBufferHandle handle)
        => new(descriptor, layout, RenderIndexFormat.UInt32, BgfxBufferKind.Vertex, handle.idx);

    public static BgfxBufferResource FromIndex(
        RenderBufferDescriptor descriptor,
        RenderIndexFormat format,
        bgfx.IndexBufferHandle handle)
        => new(descriptor, null, format, BgfxBufferKind.Index, handle.idx);

    public static BgfxBufferResource FromDynamicVertex(
        RenderBufferDescriptor descriptor,
        RenderVertexLayout? layout,
        bgfx.DynamicVertexBufferHandle handle)
        => new(descriptor, layout, RenderIndexFormat.UInt32, BgfxBufferKind.DynamicVertex, handle.idx);

    public static BgfxBufferResource FromDynamicIndex(
        RenderBufferDescriptor descriptor,
        RenderIndexFormat format,
        bgfx.DynamicIndexBufferHandle handle)
        => new(descriptor, null, format, BgfxBufferKind.DynamicIndex, handle.idx);

    public static BgfxBufferResource FromIndirect(
        RenderBufferDescriptor descriptor,
        bgfx.IndirectBufferHandle handle)
        => new(descriptor, null, RenderIndexFormat.UInt32, BgfxBufferKind.Indirect, handle.idx);
}

internal sealed class BgfxShaderBindingResource
{
    public BgfxShaderBindingResource(
        RenderShaderBindingDescriptor descriptor,
        bgfx.UniformHandle uniform)
    {
        this.descriptor = descriptor;
        this.uniform = uniform;
    }

    public RenderShaderBindingDescriptor descriptor { get; }
    public bgfx.UniformHandle uniform { get; }
}

internal sealed class BgfxPipelineResource
{
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

    public bgfx.ProgramHandle program { get; }
    public IReadOnlyDictionary<string, BgfxShaderBindingResource> bindings { get; }
    public RenderVertexLayout? vertexLayout { get; }
    public bgfx.VertexLayoutHandle vertexLayoutHandle { get; }
    public RenderRasterState? rasterState { get; }
    public bool compute { get; }
}
