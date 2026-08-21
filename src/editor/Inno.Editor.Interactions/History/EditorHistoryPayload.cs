using System;
using System.IO;

namespace Inno.Editor.Interactions;

/// <summary>
/// Owns immutable neutral bytes used by a reload-safe editor history change.
/// </summary>
public sealed class EditorHistoryPayload : IDisposable
{
    private byte[]? m_inlineBytes;
    private EditorHistoryBlobStore? m_store;
    private string? m_path;
    private bool m_disposed;

    private EditorHistoryPayload(byte[] bytes)
    {
        m_inlineBytes = bytes;
        length = bytes.LongLength;
    }

    private EditorHistoryPayload(EditorHistoryBlobStore store, string path, long length)
    {
        m_store = store;
        m_path = path;
        this.length = length;
    }

    /// <summary>
    /// Gets the number of immutable bytes represented by this payload.
    /// </summary>
    public long length { get; }

    /// <summary>
    /// Gets whether the payload is retained in the temporary disk store instead of resident memory.
    /// </summary>
    public bool isStoredOnDisk => m_path is not null;

    internal long residentSize => m_inlineBytes?.LongLength ?? 0L;

    internal long diskSize => m_path is null ? 0L : length;

    /// <summary>
    /// Creates an immutable history payload by copying the supplied bytes.
    /// </summary>
    /// <param name="bytes">The neutral bytes to retain for future Undo and Redo transitions.</param>
    /// <returns>A new independently owned payload.</returns>
    public static EditorHistoryPayload FromBytes(ReadOnlySpan<byte> bytes)
        => new(bytes.ToArray());

    /// <summary>
    /// Reads the complete immutable payload into a newly allocated byte array.
    /// </summary>
    /// <returns>A copy of the payload bytes.</returns>
    /// <exception cref="ObjectDisposedException">Thrown after the payload has been disposed.</exception>
    public byte[] ReadBytes()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_inlineBytes is not null)
            return (byte[])m_inlineBytes.Clone();
        return File.ReadAllBytes(m_path!);
    }

    internal EditorHistoryPayload Retain(
        EditorHistoryBlobStore store,
        int inlinePayloadThreshold,
        bool canStoreOnDisk)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        byte[] bytes = ReadBytes();
        if (!canStoreOnDisk || bytes.Length < inlinePayloadThreshold)
            return new EditorHistoryPayload(bytes);
        string path = store.Retain(bytes);
        return new EditorHistoryPayload(store, path, bytes.LongLength);
    }

    /// <summary>
    /// Releases the resident or temporary disk storage owned by this payload.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        if (m_store is not null && m_path is not null)
            m_store.Release(m_path);
        m_inlineBytes = null;
        m_store = null;
        m_path = null;
        GC.SuppressFinalize(this);
    }
}
