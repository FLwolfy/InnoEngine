using System;
using System.Collections.Generic;

namespace Inno.Assets;

/// <summary>
/// Contains one atomically committed asset database revision.
/// </summary>
public sealed class AssetChangeSet
{
    /// <summary>
    /// Creates a committed change set.
    /// </summary>
    /// <param name="revision">
    /// The monotonic database revision assigned to the atomic commit.
    /// </param>
    /// <param name="changes">
    /// The ordered changes published by the commit, or <see langword="null"/> for an empty commit.
    /// </param>
    public AssetChangeSet(long revision, IReadOnlyList<AssetChange>? changes)
    {
        this.revision = revision;
        this.changes = changes ?? Array.Empty<AssetChange>();
    }

    /// <summary>
    /// Gets the monotonically increasing database revision.
    /// </summary>
    public long revision { get; }

    /// <summary>
    /// Gets the changes committed by this revision.
    /// </summary>
    public IReadOnlyList<AssetChange> changes { get; }

    /// <summary>
    /// Gets whether the change set contains no changes.
    /// </summary>
    public bool isEmpty => changes.Count == 0;
}
