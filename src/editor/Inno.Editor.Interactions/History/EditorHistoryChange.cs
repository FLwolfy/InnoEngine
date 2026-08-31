using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Describes one neutral reload-safe mutation interpreted by an attribute-discovered history handler.
/// </summary>
public sealed class EditorHistoryChange : IDisposable
{
    private bool m_disposed;

    /// <summary>
    /// Creates a neutral history change.
    /// </summary>
    /// <param name="kind">The stable globally unique handler protocol identifier.</param>
    /// <param name="payload">The immutable neutral payload interpreted by the handler.</param>
    /// <param name="mergeKey">An optional stable key used by the handler to merge adjacent changes.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="kind"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="payload"/> is <see langword="null"/>.</exception>
    public EditorHistoryChange(
        string kind,
        EditorHistoryPayload payload,
        string? mergeKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        this.kind = kind;
        this.payload = payload ?? throw new ArgumentNullException(nameof(payload));
        this.mergeKey = string.IsNullOrWhiteSpace(mergeKey) ? null : mergeKey;
    }

    /// <summary>
    /// Gets the stable globally unique handler protocol identifier.
    /// </summary>
    public string kind { get; }

    /// <summary>
    /// Gets the immutable neutral payload interpreted by the active handler generation.
    /// </summary>
    public EditorHistoryPayload payload { get; }

    /// <summary>
    /// Gets the optional stable key used to merge adjacent changes to the same logical value.
    /// </summary>
    public string? mergeKey { get; }

    internal long residentSize => payload.residentSize;

    internal long diskSize => payload.diskSize;

    internal EditorHistoryChange Retain(
        EditorHistoryBlobStore store,
        int inlinePayloadThreshold,
        bool canStoreOnDisk)
        => new(
            kind,
            payload.Retain(store, inlinePayloadThreshold, canStoreOnDisk),
            mergeKey);

    /// <summary>
    /// Releases the payload storage owned by this history change.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        payload.Dispose();
        GC.SuppressFinalize(this);
    }
}
