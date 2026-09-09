using Inno.Core.Diagnostics;
using System;
using Inno.Core.Execution;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inno.Rendering;

namespace Inno.Rendering.Runtime;

internal sealed class RenderResourceService : RenderResourceProvider, IRenderResourceService, IDisposable
{
    private const ulong C_UNUSED_FRAME_LIMIT = 240;

    private readonly IRenderDevice m_device;
    private readonly IDiagnosticReporter m_diagnostics;
    private readonly IRenderTargetArtifactProvider? m_targetArtifacts;
    private readonly RenderResourceCache<RenderPersistentResourceId, BufferEntry> m_buffers;
    private readonly RenderResourceCache<RenderPersistentResourceId, TextureEntry> m_textures;
    private readonly RenderResourceCache<RenderPersistentResourceId, GraphicsPipelineEntry> m_graphicsPipelines;
    private readonly RenderResourceCache<RenderPersistentResourceId, ComputePipelineEntry> m_computePipelines;
    private readonly RenderGeometryOwner m_geometry;
    private readonly RenderMaterialOwner m_materials;
    private readonly RenderReadbackOwner m_readbacks;
    private ulong m_frameIndex;
    private bool m_disposed;
    private RenderRetirementQueue? m_retirement;
    private RenderRetirementQueue? m_failedAllocation;

    internal RenderResourceService(
        IRenderDevice device,
        IDiagnosticReporter diagnostics,
        IRenderTargetArtifactProvider? targetArtifacts, RenderResourceLimits limits)
    {
        m_device = device ?? throw new ArgumentNullException(nameof(device));
        m_diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        m_targetArtifacts = targetArtifacts;
        m_readbacks = new(device, limits.pendingReadbacks);
        m_buffers = new(entry => m_device.DestroyBuffer(entry.handle), limits.resourcesPerKind);
        m_textures = new(entry => m_device.DestroyTexture(entry.handle), limits.resourcesPerKind);
        m_graphicsPipelines = new(entry => m_device.DestroyGraphicsPipeline(entry.handle), limits.resourcesPerKind);
        m_computePipelines = new(entry => m_device.DestroyComputePipeline(entry.handle), limits.resourcesPerKind);
        m_geometry = new(device, diagnostics, limits.resourcesPerKind);
        m_materials = new(device, diagnostics, targetArtifacts, TryResolveTexture, limits.resourcesPerKind);
    }

    /// <summary>
    /// Gets the immutable feature and limit set reported by the active graphics backend.
    /// </summary>
    public GraphicsCapabilities capabilities => m_device.capabilities;

    /// <summary>
    /// Creates runtime pipeline and binding resources for the material before first use.
    /// </summary>
    /// <param name="material">
    /// The material consumed by prewarm material; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public void PrewarmMaterial(MaterialAsset material)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(material);
        ShaderAsset shader = material.shader
            ?? throw new ArgumentException("A material must reference a shader before prewarming.", nameof(material));
        RenderShaderVariant variant = RenderShaderVariant.FromMaterial(material);
        if (m_targetArtifacts is null)
            throw new InvalidOperationException("No render target artifact provider is configured for this runtime.");
        _ = m_targetArtifacts.GetShaderArtifact(shader, variant, m_device.capabilities, out _);
    }

    /// <summary>
    /// Creates runtime GPU resources for the texture before its first render use.
    /// </summary>
    /// <param name="texture">
    /// The texture consumed by prewarm texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public void PrewarmTexture(TextureAsset texture)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(texture);
        PrewarmTextureArtifact(texture.GetTextureArtifactReference());
    }

    /// <summary>
    /// Queues target conversion for one stable texture artifact slot.
    /// </summary>
    /// <param name="texture">
    /// Stable artifact reference to prepare.
    /// </param>
    public void PrewarmTextureArtifact(RenderTextureArtifactReference texture)
    {
        ThrowIfDisposed();
        if (texture.assetId == Guid.Empty || string.IsNullOrWhiteSpace(texture.slot.id))
            throw new ArgumentException("A valid texture artifact reference is required.", nameof(texture));
        if (m_targetArtifacts is null)
            throw new InvalidOperationException("No render target artifact provider is configured for this runtime.");
        _ = m_targetArtifacts.GetTextureArtifact(texture, out _);
    }

    /// <summary>
    /// Acquires caller-owned access to buffer until the returned lifetime is released.
    /// </summary>
    /// <param name="id">
    /// The stable identity used to locate the requested value.
    /// </param>
    /// <param name="revision">
    /// The revision consumed by acquire buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="descriptor">
    /// The descriptor consumed by acquire buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="initialData">
    /// The initial data consumed by acquire buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="name">
    /// The human-readable name used for presentation and diagnostics.
    /// </param>
    /// <returns>
    /// The validated persistent buffer handle that represents the completed operation.
    /// </returns>
    public PersistentBufferHandle AcquireBuffer(
        RenderPersistentResourceId id,
        long revision,
        PersistentBufferDescriptor descriptor,
        ReadOnlyMemory<byte> initialData,
        string name)
    {
        ThrowIfDisposed();
        RequireId(id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (m_buffers.TryGetValue(id, out BufferEntry? current)
            && current.revision == revision
            && BufferDescriptorsEqual(current.descriptor, descriptor))
        {
            current.lastUsedFrame = m_frameIndex;
            return current.handle;
        }

        m_buffers.RequireCapacity(id);
        PersistentBufferHandle candidate = m_device.CreateBuffer(descriptor, initialData.Span, name);
        var replacement = new BufferEntry(candidate, descriptor, revision, m_frameIndex);
        m_buffers.Replace(id, replacement);
        m_buffers.Drain();
        return candidate;
    }

    /// <summary>
    /// Acquires caller-owned access to texture until the returned lifetime is released.
    /// </summary>
    /// <param name="id">
    /// The stable identity used to locate the requested value.
    /// </param>
    /// <param name="revision">
    /// The revision consumed by acquire texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="descriptor">
    /// The descriptor consumed by acquire texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="subresources">
    /// The subresources consumed by acquire texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="name">
    /// The human-readable name used for presentation and diagnostics.
    /// </param>
    /// <returns>
    /// The validated persistent texture handle that represents the completed operation.
    /// </returns>
    public PersistentTextureHandle AcquireTexture(
        RenderPersistentResourceId id,
        long revision,
        RenderTextureDescriptor descriptor,
        IReadOnlyList<RenderTextureSubresourceData> subresources,
        string name)
    {
        ThrowIfDisposed();
        RequireId(id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(subresources);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (subresources.Count == 0)
            throw new ArgumentException("A raw texture requires at least one subresource upload.", nameof(subresources));
        if (m_textures.TryGetValue(id, out TextureEntry? current)
            && current.kind == TextureEntryKind.Raw
            && current.revision == revision
            && descriptor.Equals(current.descriptor))
        {
            current.lastUsedFrame = m_frameIndex;
            return current.handle;
        }

        m_textures.RequireCapacity(id);
        PersistentTextureHandle candidate = m_device.CreateTexture(descriptor, name);
        try
        {
            foreach (RenderTextureSubresourceData subresource in subresources)
            {
                if (subresource.mipLevel >= descriptor.mipCount
                    || (subresource.mipLevel < descriptor.mipCount
                        && subresource.arrayLayer
                            >= descriptor.GetSubresourceLayerCount(subresource.mipLevel)))
                {
                    throw new ArgumentException(
                        "A texture upload addresses a mip or layer outside its descriptor.",
                        nameof(subresources));
                }
                m_device.UpdateTexture(
                    candidate,
                    subresource.data.Span,
                    subresource.mipLevel,
                    subresource.arrayLayer);
            }
        }
        catch (Exception failure)
        {
            DiscardAllocation(() => m_device.DestroyTexture(candidate), failure);
            throw;
        }

        var replacement = new TextureEntry(
            candidate,
            TextureEntryKind.Raw,
            descriptor,
            revision,
            m_frameIndex);
        m_textures.Replace(id, replacement);
        m_textures.Drain();
        return candidate;
    }

    /// <summary>
    /// Acquires caller-owned access to ktx texture until the returned lifetime is released.
    /// </summary>
    /// <param name="id">
    /// The stable identity used to locate the requested value.
    /// </param>
    /// <param name="revision">
    /// The revision consumed by acquire ktx texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="containerData">
    /// The container data consumed by acquire ktx texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="sRgb">
    /// Whether s rgb behavior is enabled while acquire ktx texture executes.
    /// </param>
    /// <param name="name">
    /// The human-readable name used for presentation and diagnostics.
    /// </param>
    /// <returns>
    /// The validated persistent texture handle that represents the completed operation.
    /// </returns>
    public PersistentTextureHandle AcquireKtxTexture(
        RenderPersistentResourceId id,
        long revision,
        ReadOnlyMemory<byte> containerData,
        bool sRgb,
        string name)
    {
        ThrowIfDisposed();
        RequireId(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (containerData.IsEmpty)
            throw new ArgumentException("A KTX resource cannot be empty.", nameof(containerData));
        if (m_textures.TryGetValue(id, out TextureEntry? current)
            && current.kind == TextureEntryKind.Ktx
            && current.revision == revision
            && current.sRgb == sRgb)
        {
            current.lastUsedFrame = m_frameIndex;
            return current.handle;
        }

        m_textures.RequireCapacity(id);
        PersistentTextureHandle candidate = m_device.CreateTexture(
            RenderTextureContainer.Ktx,
            containerData.Span,
            sRgb,
            name);
        var replacement = new TextureEntry(
            candidate,
            TextureEntryKind.Ktx,
            descriptor: null,
            revision,
            m_frameIndex,
            sRgb);
        m_textures.Replace(id, replacement);
        m_textures.Drain();
        return candidate;
    }

    /// <summary>
    /// Updates texture state from the current authoritative inputs.
    /// </summary>
    /// <param name="texture">
    /// The texture consumed by update texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="region">
    /// The region consumed by update texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="data">
    /// The complete immutable byte payload consumed by this operation.
    /// </param>
    public void UpdateTexture(
        PersistentTextureHandle texture,
        RenderTextureRegion region,
        ReadOnlyMemory<byte> data)
    {
        ThrowIfDisposed();
        if (!texture.isValid)
            throw new ArgumentException("A valid persistent texture is required.", nameof(texture));
        if (data.IsEmpty)
            throw new ArgumentException("Texture update data cannot be empty.", nameof(data));
        m_device.UpdateTextureRegion(texture, region, data.Span);
    }

    /// <summary>
    /// Reads and validates the texture async value from its authoritative source.
    /// </summary>
    /// <param name="texture">
    /// The texture consumed by read texture async; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="mipLevel">
    /// The mip level consumed by read texture async; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    /// <returns>
    /// An asynchronous operation that completes after all requested work has finished.
    /// </returns>
    public ValueTask<RenderTextureReadbackResult> ReadTextureAsync(
        PersistentTextureHandle texture,
        int mipLevel = 0,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return m_readbacks.Read(texture, mipLevel, cancellationToken);
    }

    /// <summary>
    /// Attempts to resolve graphics material without changing state when the operation cannot complete.
    /// </summary>
    /// <param name="material">
    /// The material consumed by try resolve graphics material; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="contractId">
    /// The contract id consumed by try resolve graphics material; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="passRoleId">
    /// The pass role id consumed by try resolve graphics material; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="vertexLayout">
    /// The vertex layout consumed by try resolve graphics material; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="overrides">
    /// The overrides consumed by try resolve graphics material; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="materialPass">
    /// The material pass consumed by try resolve graphics material; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryResolveGraphicsMaterial(
        MaterialAsset material,
        ShaderContractId contractId,
        ShaderPassRoleId passRoleId,
        RenderVertexLayout? vertexLayout,
        MaterialPropertyBlock? overrides,
        out RenderMaterialPass? materialPass)
    {
        ThrowIfDisposed();
        return m_materials.TryResolveMaterial(
            material,
            contractId,
            passRoleId,
            ShaderProgramKind.Raster,
            vertexLayout,
            overrides,
            out materialPass);
    }

    /// <summary>
    /// Attempts to resolve compute material without changing state when the operation cannot complete.
    /// </summary>
    /// <param name="material">
    /// The material consumed by try resolve compute material; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="contractId">
    /// The contract id consumed by try resolve compute material; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="passRoleId">
    /// The pass role id consumed by try resolve compute material; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="overrides">
    /// The overrides consumed by try resolve compute material; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="materialPass">
    /// The material pass consumed by try resolve compute material; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryResolveComputeMaterial(
        MaterialAsset material,
        ShaderContractId contractId,
        ShaderPassRoleId passRoleId,
        MaterialPropertyBlock? overrides,
        out RenderMaterialPass? materialPass)
    {
        ThrowIfDisposed();
        return m_materials.TryResolveMaterial(
            material,
            contractId,
            passRoleId,
            ShaderProgramKind.Compute,
            vertexLayout: null,
            overrides,
            out materialPass);
    }

    /// <summary>
    /// Attempts to resolve geometry without changing state when the operation cannot complete.
    /// </summary>
    /// <param name="geometry">
    /// The geometry consumed by try resolve geometry; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="resolvedGeometry">
    /// The resolved geometry consumed by try resolve geometry; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryResolveGeometry(GeometryAsset geometry, out RenderGeometry? resolvedGeometry)
    {
        ThrowIfDisposed();
        return m_geometry.TryResolve(geometry, out resolvedGeometry);
    }

    /// <summary>
    /// Attempts to resolve texture without changing state when the operation cannot complete.
    /// </summary>
    /// <param name="texture">
    /// The texture consumed by try resolve texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="resolvedTexture">
    /// The resolved texture consumed by try resolve texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryResolveTexture(TextureAsset texture, out PersistentTextureHandle resolvedTexture)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(texture);
        Guid id = texture.identity.persistentId;
        if (id == Guid.Empty)
        {
            resolvedTexture = default;
            Publish("RENDER_TEXTURE_ID_MISSING", "Texture must have a persistent asset identity.", texture.assetPath.ToString());
            return false;
        }
        return TryResolveTextureArtifact(texture.GetTextureArtifactReference(), out resolvedTexture);
    }

    /// <summary>
    /// Resolves one stable texture artifact slot into a generation-scoped sampled texture.
    /// </summary>
    /// <param name="texture">
    /// Stable texture artifact reference.
    /// </param>
    /// <param name="resolvedTexture">
    /// Receives the current or last-good GPU texture when successful.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when current or last-good texture content is usable.
    /// </returns>
    public bool TryResolveTextureArtifact(
        RenderTextureArtifactReference texture,
        out PersistentTextureHandle resolvedTexture)
    {
        ThrowIfDisposed();
        resolvedTexture = default;
        if (texture.assetId == Guid.Empty || string.IsNullOrWhiteSpace(texture.slot.id))
            return false;

        RenderPersistentResourceId resourceId = texture.resourceId;
        bool sRgb = texture.slot.colorSpace == TextureColorSpace.Srgb;
        if (m_textures.TryGetValue(resourceId, out TextureEntry? current)
            && current.kind == TextureEntryKind.Ktx
            && current.revision == texture.contentRevision
            && current.sRgb == sRgb)
        {
            current.lastUsedFrame = m_frameIndex;
            resolvedTexture = current.handle;
            return true;
        }
        try
        {
            ReadOnlyMemory<byte> artifact = ReadOnlyMemory<byte>.Empty;
            RenderTargetArtifactStatus status = m_targetArtifacts is null
                ? RenderTargetArtifactStatus.Unavailable
                : m_targetArtifacts.GetTextureArtifact(texture, out artifact);
            string sourceId = $"{texture.assetId:D}:{texture.slot.id}";
            if (status != RenderTargetArtifactStatus.Unavailable)
                m_diagnostics.Resolve("RENDER_TEXTURE_TARGET_UNAVAILABLE", sourceId);
            if (status != RenderTargetArtifactStatus.Ready)
            {
                if (status == RenderTargetArtifactStatus.Unavailable)
                {
                    Publish(
                        "RENDER_TEXTURE_TARGET_UNAVAILABLE",
                        m_targetArtifacts is null
                            ? "No render target artifact provider is configured for this runtime."
                            : $"No deployed target texture artifact exists for '{sourceId}'.",
                        sourceId);
                }
                if (m_textures.TryGetValue(resourceId, out TextureEntry? lastGood))
                {
                    lastGood.lastUsedFrame = m_frameIndex;
                    resolvedTexture = lastGood.handle;
                    return true;
                }
                return false;
            }
            if (artifact.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"Target artifact provider returned an empty ready texture for '{sourceId}'.");
            }
            resolvedTexture = AcquireKtxTexture(
                resourceId,
                texture.contentRevision,
                artifact,
                sRgb,
                sourceId);
            m_diagnostics.Resolve("RENDER_TEXTURE_RESOLVE_FAILED", sourceId);
            return true;
        }
        catch (Exception pending) when (RetirementPendingException.Find(pending) is not null) { throw; }
        catch (Exception exception)
        {
            Publish(
                "RENDER_TEXTURE_RESOLVE_FAILED",
                $"Texture artifact '{texture.assetId:D}:{texture.slot.id}' kept its last-good GPU resource: {exception.Message}",
                $"{texture.assetId:D}:{texture.slot.id}");
            if (m_textures.TryGetValue(resourceId, out TextureEntry? lastGood))
            {
                lastGood.lastUsedFrame = m_frameIndex;
                resolvedTexture = lastGood.handle;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Acquires caller-owned access to graphics pipeline until the returned lifetime is released.
    /// </summary>
    /// <param name="id">
    /// The stable identity used to locate the requested value.
    /// </param>
    /// <param name="revision">
    /// The revision consumed by acquire graphics pipeline; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="descriptor">
    /// The descriptor consumed by acquire graphics pipeline; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="name">
    /// The human-readable name used for presentation and diagnostics.
    /// </param>
    /// <returns>
    /// The validated graphics pipeline handle that represents the completed operation.
    /// </returns>
    public GraphicsPipelineHandle AcquireGraphicsPipeline(
        RenderPersistentResourceId id,
        long revision,
        GraphicsPipelineDescriptor descriptor,
        string name)
    {
        ThrowIfDisposed();
        RequireId(id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (m_graphicsPipelines.TryGetValue(id, out GraphicsPipelineEntry? current)
            && current.revision == revision)
        {
            current.lastUsedFrame = m_frameIndex;
            return current.handle;
        }

        m_graphicsPipelines.RequireCapacity(id);
        GraphicsPipelineHandle candidate = m_device.CreateGraphicsPipeline(descriptor, name);
        var replacement = new GraphicsPipelineEntry(candidate, revision, m_frameIndex);
        m_graphicsPipelines.Replace(id, replacement);
        m_graphicsPipelines.Drain();
        return candidate;
    }

    /// <summary>
    /// Acquires caller-owned access to compute pipeline until the returned lifetime is released.
    /// </summary>
    /// <param name="id">
    /// The stable identity used to locate the requested value.
    /// </param>
    /// <param name="revision">
    /// The revision consumed by acquire compute pipeline; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="descriptor">
    /// The descriptor consumed by acquire compute pipeline; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="name">
    /// The human-readable name used for presentation and diagnostics.
    /// </param>
    /// <returns>
    /// The validated compute pipeline handle that represents the completed operation.
    /// </returns>
    public ComputePipelineHandle AcquireComputePipeline(
        RenderPersistentResourceId id,
        long revision,
        ComputePipelineDescriptor descriptor,
        string name)
    {
        ThrowIfDisposed();
        RequireId(id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (m_computePipelines.TryGetValue(id, out ComputePipelineEntry? current)
            && current.revision == revision)
        {
            current.lastUsedFrame = m_frameIndex;
            return current.handle;
        }

        m_computePipelines.RequireCapacity(id);
        ComputePipelineHandle candidate = m_device.CreateComputePipeline(descriptor, name);
        var replacement = new ComputePipelineEntry(candidate, revision, m_frameIndex);
        m_computePipelines.Replace(id, replacement);
        m_computePipelines.Drain();
        return candidate;
    }

    /// <summary>
    /// Releases the caller-owned value lifetime and its retained resources.
    /// </summary>
    /// <param name="id">
    /// The stable identity used to locate the requested value.
    /// </param>
    public void Release(RenderPersistentResourceId id)
    {
        ThrowIfDisposed();
        RequireId(id);
        m_graphicsPipelines.Release(id);
        m_computePipelines.Release(id);
        m_buffers.Release(id);
        m_textures.Release(id);
    }

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        if (m_retirement is null)
        {
            m_retirement = new RenderRetirementQueue();
            m_retirement.Add(m_readbacks.Dispose);
            if (m_failedAllocation is not null)
                m_retirement.Add(m_failedAllocation.Dispose);
            m_retirement.Add(m_materials.Dispose);
            m_retirement.Add(m_geometry.Dispose);
            m_retirement.Add(m_graphicsPipelines.Dispose);
            m_retirement.Add(m_computePipelines.Dispose);
            m_retirement.Add(m_buffers.Dispose);
            m_retirement.Add(m_textures.Dispose);
        }
        try { m_retirement.Dispose(); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch
        {
            CompleteRetirement();
            throw;
        }
        CompleteRetirement();
    }

    private void CompleteRetirement()
    {
        m_disposed = true;
    }

    internal RenderResourceStatistics statistics => new()
    {
        activeResources = m_buffers.Count + m_textures.Count + m_graphicsPipelines.Count + m_computePipelines.Count + m_materials.count + m_geometry.count,
        retiringResources = m_buffers.pendingCount + m_textures.pendingCount + m_graphicsPipelines.pendingCount + m_computePipelines.pendingCount + m_materials.retiringCount + m_geometry.retiringCount,
        rejectedResources = m_buffers.rejectedCount + m_textures.rejectedCount + m_graphicsPipelines.rejectedCount + m_computePipelines.rejectedCount + m_materials.rejectedCount + m_geometry.rejectedCount,
        pendingReadbacks = m_readbacks.count, peakReadbacks = m_readbacks.peakCount, rejectedReadbacks = m_readbacks.rejectedCount
    };

    internal void BeginFrame(ulong frameIndex)
    {
        ThrowIfDisposed();
        m_frameIndex = frameIndex;
        m_geometry.BeginFrame(frameIndex);
        m_materials.BeginFrame(frameIndex);
        m_readbacks.Update();
    }

    internal void SweepUnused()
    {
        ThrowIfDisposed();
        if (m_frameIndex < C_UNUSED_FRAME_LIMIT)
            return;
        ulong oldest = m_frameIndex - C_UNUSED_FRAME_LIMIT;
        m_materials.Sweep(oldest);
        m_geometry.Sweep(oldest);
        m_graphicsPipelines.Sweep(entry => entry.lastUsedFrame < oldest);
        m_computePipelines.Sweep(entry => entry.lastUsedFrame < oldest);
        m_buffers.Sweep(entry => entry.lastUsedFrame < oldest);
        m_textures.Sweep(entry => entry.lastUsedFrame < oldest);
    }

    private static bool BufferDescriptorsEqual(PersistentBufferDescriptor left, PersistentBufferDescriptor right)
        => left.buffer.Equals(right.buffer)
            && Equals(left.vertexLayout, right.vertexLayout)
            && left.indexFormat == right.indexFormat;

    private static void RequireId(RenderPersistentResourceId id)
    {
        if (!id.isValid)
            throw new ArgumentException("A persistent render resource ID must be valid.", nameof(id));
    }

    private void Publish(string code, string message, string? source)
        => m_diagnostics.Publish(new Diagnostic(
            code,
            message,
            DiagnosticSeverity.Error,
            source));

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed || m_retirement is not null, this);
        if (m_failedAllocation is not null)
        {
            m_failedAllocation.Dispose();
            m_failedAllocation = null;
        }
        m_materials.Drain();
        m_geometry.Drain();
        m_graphicsPipelines.Drain();
        m_computePipelines.Drain();
        m_buffers.Drain();
        m_textures.Drain();
    }

    private void DiscardAllocation(Action release, Exception failure)
    {
        m_failedAllocation = new RenderRetirementQueue();
        m_failedAllocation.Add(release);
        try { m_failedAllocation.Dispose(); }
        catch (Exception retirement) { throw new AggregateException("GPU allocation and retirement failed.", failure, retirement); }
        m_failedAllocation = null;
    }

    private enum TextureEntryKind
    {
        Raw,
        Ktx
    }

    private sealed class BufferEntry
    {
        internal BufferEntry(
            PersistentBufferHandle handle,
            PersistentBufferDescriptor descriptor,
            long revision,
            ulong lastUsedFrame)
        {
            this.handle = handle;
            this.descriptor = descriptor;
            this.revision = revision;
            this.lastUsedFrame = lastUsedFrame;
        }

        internal PersistentBufferHandle handle { get; }
        internal PersistentBufferDescriptor descriptor { get; }
        internal long revision { get; }
        internal ulong lastUsedFrame { get; set; }
    }

    private sealed class TextureEntry
    {
        internal TextureEntry(
            PersistentTextureHandle handle,
            TextureEntryKind kind,
            RenderTextureDescriptor? descriptor,
            long revision,
            ulong lastUsedFrame,
            bool sRgb = false)
        {
            this.handle = handle;
            this.kind = kind;
            this.descriptor = descriptor;
            this.revision = revision;
            this.lastUsedFrame = lastUsedFrame;
            this.sRgb = sRgb;
        }

        internal PersistentTextureHandle handle { get; }
        internal TextureEntryKind kind { get; }
        internal RenderTextureDescriptor? descriptor { get; }
        internal long revision { get; }
        internal bool sRgb { get; }
        internal ulong lastUsedFrame { get; set; }
    }

    private sealed class GraphicsPipelineEntry
    {
        internal GraphicsPipelineEntry(
            GraphicsPipelineHandle handle,
            long revision,
            ulong lastUsedFrame)
        {
            this.handle = handle;
            this.revision = revision;
            this.lastUsedFrame = lastUsedFrame;
        }

        internal GraphicsPipelineHandle handle { get; }
        internal long revision { get; }
        internal ulong lastUsedFrame { get; set; }
    }

    private sealed class ComputePipelineEntry
    {
        internal ComputePipelineEntry(
            ComputePipelineHandle handle,
            long revision,
            ulong lastUsedFrame)
        {
            this.handle = handle;
            this.revision = revision;
            this.lastUsedFrame = lastUsedFrame;
        }

        internal ComputePipelineHandle handle { get; }
        internal long revision { get; }
        internal ulong lastUsedFrame { get; set; }
    }

}
