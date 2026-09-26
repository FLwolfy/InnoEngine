using System;
using System.Collections.Generic;
using System.IO;
using Inno.Assets;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Gives a source-language extension access to a staged sample clone before serialized references are remapped.
/// </summary>
public sealed class AssetSampleTransformContext
{
    private readonly Dictionary<Guid, Guid> m_identityMap;
    private readonly IReadOnlyDictionary<string, (Guid oldId, Guid newId)> m_sourceIdentities;

    internal AssetSampleTransformContext(string stagedRoot, AssetPath source, AssetPath target,
        Dictionary<Guid, Guid> identityMap,
        IReadOnlyDictionary<string, (Guid oldId, Guid newId)> sourceIdentities)
    {
        this.stagedRoot = stagedRoot;
        this.source = source;
        this.target = target;
        m_identityMap = identityMap;
        m_sourceIdentities = sourceIdentities;
    }

    /// <summary>
    /// Gets the absolute private staging directory; files here are committed only after all transforms succeed.
    /// </summary>
    public string stagedRoot { get; }

    /// <summary>
    /// Gets the installed sample source path.
    /// </summary>
    public AssetPath source { get; }

    /// <summary>
    /// Gets the writable project destination path.
    /// </summary>
    public AssetPath target { get; }

    /// <summary>
    /// Gets cloned source identities, including directory and file metadata.
    /// </summary>
    public IReadOnlyDictionary<Guid, Guid> identityMap => m_identityMap;

    /// <summary>
    /// Resolves the source and clone identity of one file relative to the sample directory.
    /// </summary>
    /// <param name="relativePath">
    /// Slash-separated source-local file path below the sample directory.
    /// </param>
    /// <param name="oldId">
    /// Receives the installed source identity.
    /// </param>
    /// <param name="newId">
    /// Receives the writable clone identity.
    /// </param>
    /// <returns>
    /// Whether both identities were recorded from sample metadata.
    /// </returns>
    public bool TryGetSourceIdentity(string relativePath, out Guid oldId, out Guid newId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (m_sourceIdentities.TryGetValue(relativePath.Replace('\\', '/'), out var pair))
        {
            oldId = pair.oldId;
            newId = pair.newId;
            return true;
        }
        oldId = default;
        newId = default;
        return false;
    }

    /// <summary>
    /// Registers one source-language type replacement for subsequent structured asset rewriting.
    /// </summary>
    /// <param name="oldId">
    /// The type identity stored in installed sample assets.
    /// </param>
    /// <param name="newId">
    /// The type identity declared by the cloned source.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// A source identity is reused inconsistently.
    /// </exception>
    public void MapType(Guid oldId, Guid newId)
    {
        if (oldId == Guid.Empty || newId == Guid.Empty)
            throw new ArgumentException("Sample type identities must be non-empty.");
        if (m_identityMap.TryGetValue(oldId, out Guid existing) && existing != newId)
            throw new InvalidDataException($"Sample type identity '{oldId:D}' has conflicting clone mappings.");
        m_identityMap[oldId] = newId;
    }
}

/// <summary>
/// Rewrites a source language in a private sample clone before source assets are published.
/// </summary>
public interface IAssetSampleSourceRewriter
{
    /// <summary>
    /// Rewrites staged source files and registers any changed serialized type identities.
    /// </summary>
    /// <param name="context">
    /// The transaction-owned staged clone.
    /// </param>
    void Transform(AssetSampleTransformContext context);
}

/// <summary>
/// Registers a source-language sample transformer through the reloadable type catalog.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AssetSampleSourceRewriterAttribute : Attribute
{
    /// <summary>
    /// Creates one ordered source-language transformer declaration.
    /// </summary>
    /// <param name="id">
    /// Stable extension identity used for diagnostics and ordering.
    /// </param>
    public AssetSampleSourceRewriterAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
    }

    /// <summary>
    /// Gets the stable extension identity.
    /// </summary>
    public string id { get; }
}
