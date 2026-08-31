using System;
using System.Collections.Generic;

namespace Inno.Assets.Core;

/// <summary>Provides an immutable public snapshot of one cataloged asset.</summary>
public sealed class AssetInfo
{
    /// <summary>Creates an asset information snapshot.</summary>
    public AssetInfo(
        Guid persistentId,
        string relativePath,
        AssetSourceKind sourceKind,
        AssetImportStatus status,
        string importerId,
        Guid stableAssetTypeId,
        AssetArtifactKey artifactKey,
        AssetArtifactKey lastSuccessfulArtifactKey,
        IReadOnlyList<string>? diagnostics = null)
    {
        this.persistentId = persistentId;
        this.relativePath = relativePath ?? string.Empty;
        this.sourceKind = sourceKind;
        this.status = status;
        this.importerId = importerId ?? string.Empty;
        this.stableAssetTypeId = stableAssetTypeId;
        this.artifactKey = artifactKey;
        this.lastSuccessfulArtifactKey = lastSuccessfulArtifactKey;
        this.diagnostics = diagnostics ?? Array.Empty<string>();
    }

    /// <summary>Gets the persistent asset identity.</summary>
    public Guid persistentId { get; }

    /// <summary>Gets the current source-relative path.</summary>
    public string relativePath { get; }

    /// <summary>Gets whether the catalog entry represents a file or directory source.</summary>
    public AssetSourceKind sourceKind { get; }

    /// <summary>Gets the current import status.</summary>
    public AssetImportStatus status { get; }

    /// <summary>Gets the selected importer identifier.</summary>
    public string importerId { get; }

    /// <summary>Gets the stable imported asset type identity.</summary>
    public Guid stableAssetTypeId { get; }

    /// <summary>Gets the current committed artifact key.</summary>
    public AssetArtifactKey artifactKey { get; }

    /// <summary>Gets the most recent successfully imported artifact key.</summary>
    public AssetArtifactKey lastSuccessfulArtifactKey { get; }

    /// <summary>Gets diagnostics produced by the latest reconciliation or import.</summary>
    public IReadOnlyList<string> diagnostics { get; }
}
