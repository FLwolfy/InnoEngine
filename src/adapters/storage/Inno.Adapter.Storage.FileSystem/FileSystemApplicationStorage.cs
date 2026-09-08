using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Inno.Storage;

namespace Inno.Adapter.Storage.FileSystem;

/// <summary>
/// Implements atomic application storage inside one isolated filesystem directory.
/// </summary>
public sealed class FileSystemApplicationStorage : IApplicationStorage, IDisposable
{
    private readonly SemaphoreSlim m_gate = new(1, 1);
    private readonly StringComparison m_pathComparison;
    private readonly string m_root;
    private readonly string m_rootPrefix;
    private bool m_disposed;

    /// <summary>
    /// Creates a storage sandbox rooted at the supplied directory.
    /// </summary>
    /// <param name="rootDirectory">
    /// The directory exclusively exposed through storage keys.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the root is blank.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the root itself is a symbolic link or reparse point.
    /// </exception>
    public FileSystemApplicationStorage(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        m_root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        Directory.CreateDirectory(m_root);
        RejectLink(new DirectoryInfo(m_root));
        m_rootPrefix = m_root + Path.DirectorySeparatorChar;
        m_pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    /// <summary>
    /// Gets the absolute host directory that owns the sandbox.
    /// </summary>
    public string rootDirectory => m_root;

    /// <summary>
    /// Determines whether a regular file exists for the supplied sandbox key.
    /// </summary>
    /// <param name="key">
    /// The sandbox-relative storage key.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value exists.
    /// </returns>
    public async ValueTask<bool> ExistsAsync(
        StorageKey key,
        CancellationToken cancellationToken = default)
    {
        string path = Resolve(key);
        await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ValidateExistingPath(path, includeLeaf: true);
            return File.Exists(path);
        }
        finally
        {
            m_gate.Release();
        }
    }

    /// <summary>
    /// Reads one complete immutable value after validating every existing path component.
    /// </summary>
    /// <param name="key">
    /// The sandbox-relative storage key.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    /// <returns>
    /// The stored bytes, or <see langword="null"/> when the key does not exist.
    /// </returns>
    public async ValueTask<byte[]?> ReadAsync(
        StorageKey key,
        CancellationToken cancellationToken = default)
    {
        string path = Resolve(key);
        await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ValidateExistingPath(path, includeLeaf: true);
            return File.Exists(path)
                ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)
                : null;
        }
        finally
        {
            m_gate.Release();
        }
    }

    /// <summary>
    /// Atomically replaces one complete value through a temporary file in the target directory.
    /// </summary>
    /// <param name="key">
    /// The sandbox-relative storage key.
    /// </param>
    /// <param name="value">
    /// The complete value to commit.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    /// <returns>
    /// A task that completes after the replacement has been flushed and committed.
    /// </returns>
    public async ValueTask WriteAsync(
        StorageKey key,
        ReadOnlyMemory<byte> value,
        CancellationToken cancellationToken = default)
    {
        string path = Resolve(key);
        await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            string directory = Path.GetDirectoryName(path)
                ?? throw new IOException("A storage key did not resolve to a parent directory.");
            ValidateExistingPath(directory, includeLeaf: true);
            Directory.CreateDirectory(directory);
            ValidateExistingPath(directory, includeLeaf: true);
            ValidateExistingPath(path, includeLeaf: true);

            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(value, cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
        finally
        {
            m_gate.Release();
        }
    }

    /// <summary>
    /// Deletes one regular file without failing when it is absent.
    /// </summary>
    /// <param name="key">
    /// The sandbox-relative storage key.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an existing value was deleted.
    /// </returns>
    public async ValueTask<bool> DeleteAsync(
        StorageKey key,
        CancellationToken cancellationToken = default)
    {
        string path = Resolve(key);
        await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ValidateExistingPath(path, includeLeaf: true);
            if (!File.Exists(path))
                return false;
            File.Delete(path);
            return true;
        }
        finally
        {
            m_gate.Release();
        }
    }

    /// <summary>
    /// Enumerates regular files below an optional logical prefix in deterministic order.
    /// </summary>
    /// <param name="prefix">
    /// An optional normalized key prefix.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    /// <returns>
    /// An immutable key snapshot ordered by ordinal key value.
    /// </returns>
    public async ValueTask<IReadOnlyList<StorageKey>> ListAsync(
        StorageKey? prefix = null,
        CancellationToken cancellationToken = default)
    {
        await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var values = new List<StorageKey>();
            EnumerateDirectory(new DirectoryInfo(m_root), values, cancellationToken);
            IEnumerable<StorageKey> filtered = values;
            if (prefix is { } requestedPrefix)
            {
                string prefixValue = requestedPrefix.value;
                filtered = filtered.Where(candidate =>
                    string.Equals(candidate.value, prefixValue, StringComparison.Ordinal)
                    || candidate.value.StartsWith(prefixValue + "/", StringComparison.Ordinal));
            }
            return filtered
                .OrderBy(static key => key.value, StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            m_gate.Release();
        }
    }

    /// <summary>
    /// Releases synchronization resources retained by this adapter.
    /// </summary>
    public void Dispose()
    {
        m_gate.Wait();
        try { m_disposed = true; }
        finally { m_gate.Release(); }
        // Waiters still own this managed semaphore and must be allowed to observe disposal.
        // No OS wait handle is requested; the semaphore is reclaimed with its final waiter.
    }

    private void EnumerateDirectory(
        DirectoryInfo directory,
        List<StorageKey> values,
        CancellationToken cancellationToken)
    {
        RejectLink(directory);
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(entry);
            if (entry is DirectoryInfo childDirectory)
            {
                EnumerateDirectory(childDirectory, values, cancellationToken);
                continue;
            }
            if (entry is not FileInfo)
                continue;
            string relative = Path.GetRelativePath(m_root, entry.FullName)
                .Replace(Path.DirectorySeparatorChar, '/');
            values.Add(new StorageKey(relative));
        }
    }

    private string Resolve(StorageKey key)
    {
        ThrowIfDisposed();
        if (!key.isValid)
            throw new ArgumentException("A valid storage key is required.", nameof(key));
        string path = Path.GetFullPath(Path.Combine(
            m_root,
            key.value.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(m_rootPrefix, m_pathComparison))
            throw new ArgumentException("The storage key resolves outside the configured sandbox.", nameof(key));
        return path;
    }

    private void ValidateExistingPath(string path, bool includeLeaf)
    {
        string relative = Path.GetRelativePath(m_root, path);
        string current = m_root;
        string[] segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        int length = includeLeaf ? segments.Length : Math.Max(0, segments.Length - 1);
        for (int index = 0; index < length; index++)
        {
            current = Path.Combine(current, segments[index]);
            if (Directory.Exists(current))
                RejectLink(new DirectoryInfo(current));
            else if (File.Exists(current))
                RejectLink(new FileInfo(current));
            else
                break;
        }
    }

    private static void RejectLink(FileSystemInfo entry)
    {
        if (entry.LinkTarget is not null || (entry.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Storage sandbox path '{entry.FullName}' contains a symbolic link or reparse point.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(m_disposed, this);
}
