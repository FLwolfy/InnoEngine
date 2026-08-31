using System;

namespace Inno.Assets.Core;

/// <summary>Describes one committed asset database change.</summary>
public readonly struct AssetChange
{
    /// <summary>Creates an asset change descriptor.</summary>
    public AssetChange(
        AssetChangeKind kind,
        Guid persistentId,
        string relativePath,
        string oldRelativePath = "")
    {
        this.kind = kind;
        this.persistentId = persistentId;
        this.relativePath = relativePath ?? string.Empty;
        this.oldRelativePath = oldRelativePath ?? string.Empty;
    }

    /// <summary>Gets the change kind.</summary>
    public AssetChangeKind kind { get; }

    /// <summary>Gets the persistent identity affected by the change.</summary>
    public Guid persistentId { get; }

    /// <summary>Gets the current source-relative path.</summary>
    public string relativePath { get; }

    /// <summary>Gets the previous path for move operations.</summary>
    public string oldRelativePath { get; }
}
