using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Rendering;

/// <summary>
/// Provides protected construction operations for replaceable render resource-service implementations.
/// </summary>
/// <remarks>
/// The boundary lets a runtime resource service create generation-scoped values while their mutable
/// representation remains private to the Rendering assembly.
/// </remarks>
public abstract class RenderResourceProvider
{
    /// <summary>
    /// Represents one material binding staged by a derived resource service.
    /// </summary>
    protected readonly struct MaterialBinding
    {
        internal MaterialBinding(RenderMaterialBinding value)
        {
            this.value = value;
        }

        internal RenderMaterialBinding value { get; }
    }

    /// <summary>
    /// Creates a protected uniform binding value.
    /// </summary>
    /// <param name="id">
    /// The stable shader binding identifier.
    /// </param>
    /// <param name="data">
    /// The complete encoded uniform bytes.
    /// </param>
    /// <returns>
    /// A binding token accepted by <see cref="CreateMaterialPass"/>.
    /// </returns>
    protected static MaterialBinding CreateUniformBinding(RenderBindingId id, ReadOnlySpan<byte> data)
        => new(new RenderMaterialBinding(RenderMaterialBindingKind.Uniform, id, data.ToArray(), default, default));

    /// <summary>
    /// Creates a protected sampled-texture binding value.
    /// </summary>
    /// <param name="id">
    /// The stable shader binding identifier.
    /// </param>
    /// <param name="texture">
    /// The persistent texture handle.
    /// </param>
    /// <param name="sampler">
    /// The backend-neutral sampler state.
    /// </param>
    /// <returns>
    /// A binding token accepted by <see cref="CreateMaterialPass"/>.
    /// </returns>
    protected static MaterialBinding CreateTextureBinding(
        RenderBindingId id,
        PersistentTextureHandle texture,
        RenderSamplerState sampler)
        => new(new RenderMaterialBinding(RenderMaterialBindingKind.Texture, id, null, texture, sampler));

    /// <summary>
    /// Creates a generation-scoped resolved material pass.
    /// </summary>
    /// <param name="definition">
    /// The selected shader pass definition.
    /// </param>
    /// <param name="graphicsPipeline">
    /// The graphics pipeline handle, or an invalid handle for compute.
    /// </param>
    /// <param name="computePipeline">
    /// The compute pipeline handle, or an invalid handle for rasterization.
    /// </param>
    /// <param name="bindings">
    /// The complete material-owned binding snapshot.
    /// </param>
    /// <returns>
    /// A resolved material pass ready for command binding.
    /// </returns>
    protected static RenderMaterialPass CreateMaterialPass(
        ShaderPassDefinition definition,
        GraphicsPipelineHandle graphicsPipeline,
        ComputePipelineHandle computePipeline,
        IReadOnlyList<MaterialBinding> bindings)
        => new(
            definition,
            graphicsPipeline,
            computePipeline,
            bindings.Select(static binding => binding.value).ToArray());

    /// <summary>
    /// Creates generation-scoped resolved geometry from persistent buffers and immutable sections.
    /// </summary>
    /// <param name="vertexBuffer">
    /// The persistent vertex buffer.
    /// </param>
    /// <param name="indexBuffer">
    /// The persistent index buffer.
    /// </param>
    /// <param name="vertexLayout">
    /// The interleaved vertex layout.
    /// </param>
    /// <param name="vertexCount">
    /// The number of vertices.
    /// </param>
    /// <param name="indexCount">
    /// The number of indices.
    /// </param>
    /// <param name="sections">
    /// The independently drawable indexed ranges.
    /// </param>
    /// <returns>
    /// Resolved geometry owned by the current device generation.
    /// </returns>
    protected static RenderGeometry CreateGeometry(
        PersistentBufferHandle vertexBuffer,
        PersistentBufferHandle indexBuffer,
        RenderVertexLayout vertexLayout,
        int vertexCount,
        int indexCount,
        IReadOnlyList<RenderGeometrySection> sections)
        => new(vertexBuffer, indexBuffer, vertexLayout, vertexCount, indexCount, sections);
}
