using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Core.Identity;
using Inno.Core.Serialization;

namespace Inno.Assets.Core;

/// <summary>
/// Base runtime asset object that participates in identity and serialization.
/// </summary>
public abstract class AssetObject : ISerializable, IIdentityObject
{
    private AssetDependency[] m_dependencies = [];
    private byte[] m_runtimePayload = [];
    private bool m_isMissing;

    /// <summary>
    /// Relative path to the source file under <c>AssetManager.assetRoot</c>.
    /// </summary>
    [SerializableProperty(PropertyVisibility.Hide)]
    public string sourcePath { get; private set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash for source bytes used by import cache validation.
    /// </summary>
    [SerializableProperty(PropertyVisibility.Hide)]
    public string sourceHash { get; private set; } = string.Empty;

    /// <summary>
    /// Asset display name derived from <see cref="sourcePath"/>.
    /// </summary>
    public string name => string.IsNullOrWhiteSpace(sourcePath) ? GetType().Name : Path.GetFileName(sourcePath);

    /// <summary>
    /// Gets the persistent and runtime identity associated with this asset.
    /// </summary>
    public Identity identity => ((IIdentityObject)this).GetIdentity();

    /// <summary>
    /// Gets whether this instance preserves a reference to an asset that is currently unavailable.
    /// </summary>
    public bool isMissing => m_isMissing;

    /// <summary>
    /// Gets the persistent direct dependencies declared by this asset.
    /// </summary>
    public IReadOnlyList<AssetDependency> dependencies => m_dependencies;

    /// <summary>
    /// Runtime-ready artifact payload produced by importer.
    /// </summary>
    public ReadOnlyMemory<byte> runtimePayload => m_runtimePayload;

    /// <summary>
    /// Replaces the dependency descriptors associated with this asset.
    /// </summary>
    /// <param name="dependencies">Dependency descriptors to retain.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dependencies"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a descriptor has an empty persistent identity.</exception>
    protected void SetDependencies(IEnumerable<AssetDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        AssetDependency[] normalized = dependencies
            .GroupBy(static dependency => dependency.persistentId)
            .Select(static group => group.First())
            .OrderBy(static dependency => dependency.persistentId)
            .ToArray();
        for (int i = 0; i < normalized.Length; i++)
        {
            if (normalized[i].persistentId == Guid.Empty)
                throw new ArgumentException("Asset dependencies must have non-empty persistent identities.", nameof(dependencies));
            normalized[i].lastKnownPath ??= string.Empty;
        }

        m_dependencies = normalized;
    }

    internal void SetSourceInfo(string relativePath, string hash)
    {
        sourcePath = relativePath ?? string.Empty;
        sourceHash = hash ?? string.Empty;
    }

    internal void SetRuntimePayload(byte[] payload)
    {
        m_runtimePayload = payload ?? [];
    }

    internal void SetDependenciesInternal(IEnumerable<AssetDependency> dependencies)
        => SetDependencies(dependencies);

    internal void InitializeMissing(Guid persistentId, string lastKnownPath)
    {
        if (persistentId == Guid.Empty)
            throw new ArgumentException("A missing asset placeholder requires a persistent identity.", nameof(persistentId));

        IdentityManager.InitializePersistentIdentity(this, persistentId);
        sourcePath = lastKnownPath ?? string.Empty;
        sourceHash = string.Empty;
        m_runtimePayload = [];
        m_dependencies = [];
        m_isMissing = true;
    }
}
