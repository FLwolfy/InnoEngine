using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Core.IO;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Maps one isolated asset source to a controlled physical root.
/// </summary>
public sealed class AssetSourceMount
{
    /// <summary>
    /// Creates an asset source mount.
    /// </summary>
    /// <param name="id">
    /// Stable source identity.
    /// </param>
    /// <param name="rootPath">
    /// Absolute or resolvable physical source root.
    /// </param>
    /// <param name="isReadOnly">
    /// Whether all source mutations must be rejected.
    /// </param>
    /// <param name="dependencies">
    /// Other read-only sources this source may reference.
    /// </param>
    public AssetSourceMount(
        AssetSourceId id,
        string rootPath,
        bool isReadOnly,
        IEnumerable<AssetSourceId>? dependencies = null)
    {
        if (!id.isValid)
            throw new ArgumentException("An asset source ID must be valid.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        this.id = id;
        this.rootPath = Path.GetFullPath(rootPath);
        this.isReadOnly = isReadOnly;
        dependencySourceIds = (dependencies ?? [])
            .Where(dependency => dependency.isValid && dependency != id)
            .ToHashSet();
    }

    /// <summary>
    /// Gets the stable source identity.
    /// </summary>
    public AssetSourceId id { get; }

    /// <summary>
    /// Gets the controlled physical source root.
    /// </summary>
    public string rootPath { get; }

    /// <summary>
    /// Gets whether source mutations are forbidden.
    /// </summary>
    public bool isReadOnly { get; }

    /// <summary>
    /// Gets explicitly declared cross-source dependencies.
    /// </summary>
    public IReadOnlySet<AssetSourceId> dependencySourceIds { get; }

    /// <summary>
    /// Resolves a source-local path and rejects physical root escape.
    /// </summary>
    /// <param name="localPath">
    /// Source-local path.
    /// </param>
    /// <returns>
    /// An absolute path contained by this mount.
    /// </returns>
    public string Resolve(string localPath)
    {
        AssetPath path = new(id, localPath);
        try
        {
            return PathBoundary.Resolve(rootPath, path.localPath);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("An asset source path escaped its mount root.", exception);
        }
    }
}
