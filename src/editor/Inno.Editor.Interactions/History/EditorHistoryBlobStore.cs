using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Inno.Editor.Interactions;

internal sealed class EditorHistoryBlobStore : IDisposable
{
    private readonly Dictionary<string, int> m_references = new(StringComparer.Ordinal);
    private readonly string? m_sessionDirectory;
    private bool m_disposed;

    internal EditorHistoryBlobStore(string? cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory))
            return;
        m_sessionDirectory = Path.Combine(
            Path.GetFullPath(cacheDirectory),
            Guid.NewGuid().ToString("N"));
    }

    internal string Retain(ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_sessionDirectory is null)
            throw new InvalidOperationException("The history does not have a disk payload store.");
        Directory.CreateDirectory(m_sessionDirectory);
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string path = Path.Combine(m_sessionDirectory, hash + ".bin");
        if (!File.Exists(path))
        {
            string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(temporaryPath, bytes.ToArray());
            try
            {
                File.Move(temporaryPath, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                File.Delete(temporaryPath);
            }
        }
        m_references[path] = m_references.GetValueOrDefault(path) + 1;
        return path;
    }

    internal void Release(string path)
    {
        if (m_disposed || !m_references.TryGetValue(path, out int count))
            return;
        if (count > 1)
        {
            m_references[path] = count - 1;
            return;
        }
        _ = m_references.Remove(path);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Session cleanup retries deletion when the history is disposed.
        }
    }

    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_references.Clear();
        if (m_sessionDirectory is not null)
        {
            try
            {
                if (Directory.Exists(m_sessionDirectory))
                    Directory.Delete(m_sessionDirectory, recursive: true);
            }
            catch
            {
                // A stale session directory is safe to remove with the project's Library cache.
            }
        }
        GC.SuppressFinalize(this);
    }
}
