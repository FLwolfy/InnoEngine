using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Configures retention and payload storage for one editor history.
/// </summary>
public sealed class EditorHistoryOptions
{
    /// <summary>
    /// Gets or initializes the maximum number of committed top-level entries retained in memory.
    /// </summary>
    public int maxEntries { get; init; } = 256;

    /// <summary>
    /// Gets or initializes the maximum estimated resident payload bytes retained by the history.
    /// </summary>
    public long maxResidentBytes { get; init; } = 256L * 1024L * 1024L;

    /// <summary>
    /// Gets or initializes the maximum payload bytes retained in the temporary disk store.
    /// </summary>
    public long maxDiskBytes { get; init; } = 2L * 1024L * 1024L * 1024L;

    /// <summary>
    /// Gets or initializes the payload size at which immutable bytes are moved to the temporary disk store.
    /// </summary>
    public int inlinePayloadThreshold { get; init; } = 64 * 1024;

    /// <summary>
    /// Gets or initializes the optional directory that owns temporary history payloads for this editor session.
    /// </summary>
    public string? cacheDirectory { get; init; }

    internal void Validate()
    {
        if (maxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries), maxEntries, "History entry capacity must be positive.");
        if (maxResidentBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResidentBytes),
                maxResidentBytes,
                "Resident history capacity must be positive.");
        }
        if (maxDiskBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDiskBytes), maxDiskBytes, "Disk history capacity must be positive.");
        if (inlinePayloadThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inlinePayloadThreshold),
                inlinePayloadThreshold,
                "The inline payload threshold cannot be negative.");
        }
    }
}
