using System;
using System.Collections.Generic;

namespace Inno.Rendering;

/// <summary>
/// Declares an immutable source artifact that is compiled into one sampled texture slot.
/// </summary>
public readonly record struct RenderTextureArtifactSlot
{
    /// <summary>
    /// Creates one stable texture slot declaration.
    /// </summary>
    /// <param name="id">
    /// Asset-local stable slot identifier.
    /// </param>
    /// <param name="sourceOutputName">
    /// Named source artifact written by the owning asset importer.
    /// </param>
    /// <param name="colorSpace">
    /// Sampling color-space interpretation used by the target compiler and GPU resource.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> or <paramref name="sourceOutputName"/> is empty.
    /// </exception>
    public RenderTextureArtifactSlot(
        string id,
        string sourceOutputName,
        TextureColorSpace colorSpace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceOutputName);
        this.id = id;
        this.sourceOutputName = sourceOutputName;
        this.colorSpace = colorSpace;
    }

    /// <summary>
    /// Gets the asset-local stable slot identifier.
    /// </summary>
    public string id { get; }

    /// <summary>
    /// Gets the named portable image artifact consumed by target compilation.
    /// </summary>
    public string sourceOutputName { get; }

    /// <summary>
    /// Gets the sampling color-space interpretation.
    /// </summary>
    public TextureColorSpace colorSpace { get; }
}

/// <summary>
/// Identifies one immutable texture source across authoring, deployment, and runtime generations.
/// </summary>
public readonly record struct RenderTextureArtifactReference
{
    /// <summary>
    /// Creates a reference to one texture slot owned by an imported asset generation.
    /// </summary>
    /// <param name="assetId">
    /// Persistent identity of the asset that owns the named source artifact.
    /// </param>
    /// <param name="contentRevision">
    /// Current committed content revision of the owning asset.
    /// </param>
    /// <param name="slot">
    /// Stable texture slot declaration.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="assetId"/> is empty or <paramref name="contentRevision"/> is negative.
    /// </exception>
    public RenderTextureArtifactReference(
        Guid assetId,
        long contentRevision,
        RenderTextureArtifactSlot slot)
    {
        if (assetId == Guid.Empty)
            throw new ArgumentException("A texture artifact reference requires a persistent asset identity.", nameof(assetId));
        ArgumentOutOfRangeException.ThrowIfNegative(contentRevision);
        if (string.IsNullOrWhiteSpace(slot.id) || string.IsNullOrWhiteSpace(slot.sourceOutputName))
            throw new ArgumentException("A texture artifact reference requires a valid slot.", nameof(slot));
        this.assetId = assetId;
        this.contentRevision = contentRevision;
        this.slot = slot;
    }

    /// <summary>
    /// Gets the persistent identity of the owning asset.
    /// </summary>
    public Guid assetId { get; }

    /// <summary>
    /// Gets the committed owner content revision used for last-good replacement.
    /// </summary>
    public long contentRevision { get; }

    /// <summary>
    /// Gets the referenced stable texture slot.
    /// </summary>
    public RenderTextureArtifactSlot slot { get; }

    /// <summary>
    /// Gets the stable provider-owned GPU resource identity for this texture slot.
    /// </summary>
    public RenderPersistentResourceId resourceId
        => new($"asset:{assetId:D}:texture:{slot.id}");
}

/// <summary>
/// Exposes one or more portable image artifacts owned by an imported asset.
/// </summary>
public interface IRenderTextureArtifactSource
{
    /// <summary>
    /// Gets the complete immutable set of stable texture slots owned by the current asset content.
    /// </summary>
    IReadOnlyList<RenderTextureArtifactSlot> textureArtifacts { get; }
}
