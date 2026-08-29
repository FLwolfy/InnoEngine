using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inno.Rendering.Core;

namespace Inno.Rendering;

/// <summary>Identifies one provider-owned persistent GPU resource without exposing a native handle.</summary>
public readonly record struct RenderPersistentResourceId
{
    /// <summary>Creates a globally stable persistent resource identifier.</summary>
    /// <param name="value">Provider-qualified stable identifier.</param>
    public RenderPersistentResourceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>Gets the provider-qualified stable identifier.</summary>
    public string value { get; }

    /// <summary>Gets whether the identifier contains a usable value.</summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <inheritdoc />
    public override string ToString() => value ?? string.Empty;
}

/// <summary>Stores one complete texture mip and addressable layer, slice, or cubemap-face upload.</summary>
public sealed class RenderTextureSubresourceData
{
    private readonly byte[] m_data;

    /// <summary>Creates one immutable texture subresource upload.</summary>
    /// <param name="mipLevel">Zero-based mip level.</param>
    /// <param name="arrayLayer">
    /// Zero-based 2D array layer, 3D Z slice, or flattened cubemap face using cube-layer * 6 + face.
    /// </param>
    /// <param name="data">Tightly packed complete subresource bytes.</param>
    public RenderTextureSubresourceData(int mipLevel, int arrayLayer, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mipLevel);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayLayer);
        if (data.IsEmpty)
            throw new ArgumentException("A texture subresource upload cannot be empty.", nameof(data));
        this.mipLevel = mipLevel;
        this.arrayLayer = arrayLayer;
        m_data = data.ToArray();
    }

    /// <summary>Gets the zero-based mip level.</summary>
    public int mipLevel { get; }

    /// <summary>Gets the zero-based array layer, volume slice, or flattened cubemap face.</summary>
    public int arrayLayer { get; }

    /// <summary>Gets immutable tightly packed bytes.</summary>
    public ReadOnlyMemory<byte> data => m_data;
}

/// <summary>Describes one indexed range in resolved backend-neutral geometry.</summary>
public readonly record struct RenderGeometrySection
{
    /// <summary>Creates one indexed geometry range.</summary>
    /// <param name="firstIndex">First index in the shared index buffer.</param>
    /// <param name="indexCount">Positive index count.</param>
    public RenderGeometrySection(int firstIndex, int indexCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(indexCount);
        this.firstIndex = firstIndex;
        this.indexCount = indexCount;
    }

    /// <summary>Gets the first index in the shared index buffer.</summary>
    public int firstIndex { get; }

    /// <summary>Gets the number of indices in this range.</summary>
    public int indexCount { get; }
}

/// <summary>Provides generation-scoped GPU buffers for an imported geometry helper asset.</summary>
public sealed class RenderGeometry
{
    private readonly IReadOnlyList<RenderGeometrySection> m_sections;

    internal RenderGeometry(
        PersistentBufferHandle vertexBuffer,
        PersistentBufferHandle indexBuffer,
        RenderVertexLayout vertexLayout,
        int vertexCount,
        int indexCount,
        IReadOnlyList<RenderGeometrySection> sections)
    {
        this.vertexBuffer = vertexBuffer;
        this.indexBuffer = indexBuffer;
        this.vertexLayout = vertexLayout;
        this.vertexCount = vertexCount;
        this.indexCount = indexCount;
        m_sections = sections;
    }

    /// <summary>Gets the persistent vertex buffer.</summary>
    public PersistentBufferHandle vertexBuffer { get; }

    /// <summary>Gets the persistent index buffer.</summary>
    public PersistentBufferHandle indexBuffer { get; }

    /// <summary>Gets the imported interleaved vertex layout.</summary>
    public RenderVertexLayout vertexLayout { get; }

    /// <summary>Gets the number of vertices.</summary>
    public int vertexCount { get; }

    /// <summary>Gets the total index count.</summary>
    public int indexCount { get; }

    /// <summary>Gets independently drawable indexed ranges.</summary>
    public IReadOnlyList<RenderGeometrySection> sections => m_sections;

    /// <summary>Binds both geometry streams at their first element.</summary>
    /// <param name="commands">Current pass command encoder.</param>
    public void Bind(RenderCommandEncoder commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.BindVertexBuffer(vertexBuffer);
        commands.BindIndexBuffer(indexBuffer);
    }

    /// <summary>Binds and draws one indexed section.</summary>
    /// <param name="commands">Current raster pass command encoder.</param>
    /// <param name="sectionIndex">Zero-based section index.</param>
    /// <param name="instanceCount">Positive instance count.</param>
    public void DrawSection(RenderCommandEncoder commands, int sectionIndex, int instanceCount = 1)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(instanceCount);
        if ((uint)sectionIndex >= (uint)m_sections.Count)
            throw new ArgumentOutOfRangeException(nameof(sectionIndex));
        RenderGeometrySection section = m_sections[sectionIndex];
        commands.BindVertexBuffer(vertexBuffer);
        commands.BindIndexBuffer(indexBuffer, section.firstIndex);
        commands.DrawIndexed(section.indexCount, instanceCount);
    }
}

internal enum RenderMaterialBindingKind
{
    Uniform,
    Texture
}

internal sealed record RenderMaterialBinding(
    RenderMaterialBindingKind kind,
    RenderBindingId id,
    byte[]? uniformData,
    PersistentTextureHandle texture,
    RenderSamplerState sampler);

/// <summary>
/// Represents one frame-resolved material pass and its material-owned bindings.
/// </summary>
/// <remarks>
/// The instance is scoped to the active render device generation. Pipeline-owned graph textures and
/// buffers remain explicit and are bound by the caller after this material state is applied.
/// </remarks>
public sealed class RenderMaterialPass
{
    private readonly IReadOnlyList<RenderMaterialBinding> m_bindings;

    internal RenderMaterialPass(
        ShaderPassDefinition definition,
        GraphicsPipelineHandle graphicsPipeline,
        ComputePipelineHandle computePipeline,
        IReadOnlyList<RenderMaterialBinding> bindings)
    {
        this.definition = definition;
        this.graphicsPipeline = graphicsPipeline;
        this.computePipeline = computePipeline;
        m_bindings = bindings;
    }

    /// <summary>Gets the selected provider-defined shader pass.</summary>
    public ShaderPassDefinition definition { get; }

    /// <summary>Gets the graphics pipeline, or an invalid handle for a compute pass.</summary>
    public GraphicsPipelineHandle graphicsPipeline { get; }

    /// <summary>Gets the compute pipeline, or an invalid handle for a raster pass.</summary>
    public ComputePipelineHandle computePipeline { get; }

    /// <summary>Gets whether this is a graphics material pass.</summary>
    public bool isGraphics => graphicsPipeline.isValid;

    /// <summary>Gets whether this is a compute material pass.</summary>
    public bool isCompute => computePipeline.isValid;

    /// <summary>Binds the program and all material-owned values and textures.</summary>
    /// <param name="commands">Current pass command encoder.</param>
    public void Bind(RenderCommandEncoder commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (graphicsPipeline.isValid)
            commands.BindGraphicsPipeline(graphicsPipeline);
        else if (computePipeline.isValid)
            commands.BindComputePipeline(computePipeline);
        else
            throw new InvalidOperationException("A resolved material pass has no active program.");

        foreach (RenderMaterialBinding binding in m_bindings)
        {
            if (binding.kind == RenderMaterialBindingKind.Uniform)
                commands.SetUniform(binding.id, binding.uniformData ?? []);
            else
                commands.BindTexture(binding.id, binding.texture, binding.sampler);
        }
    }
}

/// <summary>
/// Resolves neutral assets and provider-owned uploads into opaque resources for the active device generation.
/// </summary>
public interface IRenderResourceService
{
    /// <summary>Gets the active backend-neutral capability snapshot.</summary>
    GraphicsCapabilities capabilities { get; }

    /// <summary>Queues all target shader work required by a material without blocking the render thread.</summary>
    /// <param name="material">Material whose selected static variant should be prepared.</param>
    void PrewarmMaterial(MaterialAsset material);

    /// <summary>Queues target texture conversion without blocking the render thread.</summary>
    /// <param name="texture">Texture source to prepare.</param>
    void PrewarmTexture(TextureAsset texture);

    /// <summary>Acquires or atomically replaces a provider-owned persistent buffer.</summary>
    /// <param name="id">Globally stable provider-owned resource ID.</param>
    /// <param name="revision">Provider-controlled content revision.</param>
    /// <param name="descriptor">Complete buffer descriptor.</param>
    /// <param name="initialData">Complete initial bytes.</param>
    /// <param name="name">Diagnostic name.</param>
    /// <returns>A handle owned by the active device generation.</returns>
    PersistentBufferHandle AcquireBuffer(
        RenderPersistentResourceId id,
        long revision,
        PersistentBufferDescriptor descriptor,
        ReadOnlyMemory<byte> initialData,
        string name);

    /// <summary>Acquires or atomically replaces a provider-owned persistent texture.</summary>
    /// <param name="id">Globally stable provider-owned resource ID.</param>
    /// <param name="revision">Provider-controlled content revision.</param>
    /// <param name="descriptor">Complete texture descriptor.</param>
    /// <param name="subresources">Complete mip and layer uploads.</param>
    /// <param name="name">Diagnostic name.</param>
    /// <returns>A handle owned by the active device generation.</returns>
    PersistentTextureHandle AcquireTexture(
        RenderPersistentResourceId id,
        long revision,
        RenderTextureDescriptor descriptor,
        IReadOnlyList<RenderTextureSubresourceData> subresources,
        string name);

    /// <summary>Acquires or atomically replaces a sampled texture from a portable KTX container.</summary>
    /// <param name="id">Globally stable provider-owned resource ID.</param>
    /// <param name="revision">Provider-controlled content revision.</param>
    /// <param name="containerData">Complete validated KTX bytes.</param>
    /// <param name="sRgb">Whether texture sampling performs sRGB decoding.</param>
    /// <param name="name">Diagnostic name.</param>
    /// <returns>A handle owned by the active device generation.</returns>
    PersistentTextureHandle AcquireKtxTexture(
        RenderPersistentResourceId id,
        long revision,
        ReadOnlyMemory<byte> containerData,
        bool sRgb,
        string name);

    /// <summary>Updates a rectangular region of an active persistent texture without recreating it.</summary>
    /// <param name="texture">Texture owned by the active device generation.</param>
    /// <param name="region">Destination mip, texel rectangle, and layer range.</param>
    /// <param name="data">Tightly packed region bytes.</param>
    void UpdateTexture(
        PersistentTextureHandle texture,
        RenderTextureRegion region,
        ReadOnlyMemory<byte> data);

    /// <summary>Asynchronously copies one complete persistent texture mip into CPU-visible memory.</summary>
    /// <param name="texture">Texture created with <see cref="RenderTextureUsage.Readback"/>.</param>
    /// <param name="mipLevel">Zero-based mip level.</param>
    /// <param name="cancellationToken">Cancellation for the caller's wait.</param>
    /// <returns>The immutable readback result after a later GPU frame completes it.</returns>
    ValueTask<RenderTextureReadbackResult> ReadTextureAsync(
        PersistentTextureHandle texture,
        int mipLevel = 0,
        CancellationToken cancellationToken = default);

    /// <summary>Acquires or atomically replaces a provider-owned graphics pipeline.</summary>
    /// <param name="id">Globally stable provider-owned resource ID.</param>
    /// <param name="revision">Provider-controlled content revision.</param>
    /// <param name="descriptor">Complete target program, interface, layout, and raster state.</param>
    /// <param name="name">Diagnostic name.</param>
    /// <returns>A graphics pipeline owned by the active device generation.</returns>
    GraphicsPipelineHandle AcquireGraphicsPipeline(
        RenderPersistentResourceId id,
        long revision,
        GraphicsPipelineDescriptor descriptor,
        string name);

    /// <summary>Acquires or atomically replaces a provider-owned compute pipeline.</summary>
    /// <param name="id">Globally stable provider-owned resource ID.</param>
    /// <param name="revision">Provider-controlled content revision.</param>
    /// <param name="descriptor">Complete target compute program and interface.</param>
    /// <param name="name">Diagnostic name.</param>
    /// <returns>A compute pipeline owned by the active device generation.</returns>
    ComputePipelineHandle AcquireComputePipeline(
        RenderPersistentResourceId id,
        long revision,
        ComputePipelineDescriptor descriptor,
        string name);

    /// <summary>Resolves one graphics material pass through an open provider contract and role.</summary>
    /// <param name="material">Material asset to resolve.</param>
    /// <param name="contractId">Provider-owned shader contract.</param>
    /// <param name="passRoleId">Provider-owned pass role.</param>
    /// <param name="vertexLayout">Expected vertex layout, or null for procedural geometry.</param>
    /// <param name="overrides">Optional frame-local material values.</param>
    /// <param name="materialPass">Receives the resolved pass when successful.</param>
    /// <returns>True when a current or last-good pass is usable.</returns>
    bool TryResolveGraphicsMaterial(
        MaterialAsset material,
        ShaderContractId contractId,
        ShaderPassRoleId passRoleId,
        RenderVertexLayout? vertexLayout,
        MaterialPropertyBlock? overrides,
        out RenderMaterialPass? materialPass);

    /// <summary>Resolves one compute material pass through an open provider contract and role.</summary>
    /// <param name="material">Material asset to resolve.</param>
    /// <param name="contractId">Provider-owned shader contract.</param>
    /// <param name="passRoleId">Provider-owned pass role.</param>
    /// <param name="overrides">Optional frame-local material values.</param>
    /// <param name="materialPass">Receives the resolved pass when successful.</param>
    /// <returns>True when a current or last-good pass is usable.</returns>
    bool TryResolveComputeMaterial(
        MaterialAsset material,
        ShaderContractId contractId,
        ShaderPassRoleId passRoleId,
        MaterialPropertyBlock? overrides,
        out RenderMaterialPass? materialPass);

    /// <summary>Resolves imported helper geometry into persistent vertex and index buffers.</summary>
    /// <param name="geometry">Imported geometry asset.</param>
    /// <param name="resolvedGeometry">Receives generation-scoped GPU geometry when successful.</param>
    /// <returns>True when current or last-good geometry is usable.</returns>
    bool TryResolveGeometry(GeometryAsset geometry, out RenderGeometry? resolvedGeometry);

    /// <summary>Resolves an imported texture into a persistent sampled texture.</summary>
    /// <param name="texture">Imported texture asset.</param>
    /// <param name="resolvedTexture">Receives a generation-scoped texture handle when successful.</param>
    /// <returns>True when current or last-good texture content is usable.</returns>
    bool TryResolveTexture(TextureAsset texture, out PersistentTextureHandle resolvedTexture);

    /// <summary>Releases any cached resource with the specified provider-owned identifier.</summary>
    /// <param name="id">Stable resource identifier to release.</param>
    void Release(RenderPersistentResourceId id);
}
