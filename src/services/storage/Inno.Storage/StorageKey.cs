using System;

namespace Inno.Storage;

/// <summary>
/// Identifies one sandbox-relative application storage value.
/// </summary>
public readonly record struct StorageKey
{
    /// <summary>
    /// Creates a normalized storage key from slash-separated path segments.
    /// </summary>
    /// <param name="value">
    /// The non-rooted key with no empty, current, or parent segments.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> can escape or ambiguously address the storage root.
    /// </exception>
    public StorageKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Replace('\\', '/').Trim('/');
        string[] segments = normalized.Split('/');
        if (normalized.Length == 0 ||
            value.StartsWith('/') ||
            value.StartsWith('\\') ||
            segments.Length == 0 ||
            Array.Exists(segments, static segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.Contains(':')))
        {
            throw new ArgumentException("A storage key must be a non-rooted unambiguous sandbox path.", nameof(value));
        }
        this.value = normalized;
    }

    /// <summary>
    /// Gets the normalized slash-separated key value.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this value contains a valid storage key.
    /// </summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats this key for diagnostics and persistence.
    /// </summary>
    /// <returns>
    /// The normalized key value.
    /// </returns>
    public override string ToString() => value ?? string.Empty;
}
