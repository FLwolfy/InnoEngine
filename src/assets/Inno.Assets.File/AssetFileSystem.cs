using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Inno.Core.Storage;

namespace Inno.Assets.File;

/// <summary>
/// Indexed asset source filesystem model backed by <see cref="AssetWatcher"/>.
/// </summary>
public sealed class AssetFileSystem : IDisposable
{
    private readonly ObjectPool<AssetFileEntry> m_entries = new();
    private readonly PoolKey<string> m_pathKey;
    private readonly PoolKey<string> m_parentPathKey;
    private readonly PoolKey<bool> m_isDirectoryKey;
    private readonly PoolKey<string> m_extensionKey;
    private readonly Lock m_sync = new();
    private readonly AssetWatcher m_watcher;
    private bool m_disposed;

    /// <summary>Gets the absolute source asset root.</summary>
    public string assetRoot { get; }
    
    /// <summary>Gets whether source file watching is active.</summary>
    public bool isWatching => m_watcher.isWatching;

    /// <summary>
    /// Raised after batched source file-system changes are detected and index refresh completes.
    /// </summary>
    public event Action<IReadOnlyList<AssetChangedEvent>>? ChangedBatch;

    /// <summary>Creates an indexed source file system.</summary>
    /// <param name="assetRoot">The absolute source root.</param>
    /// <param name="autoStart">Whether file watching should start immediately.</param>
    /// <param name="flushDelayMs">The watcher batch delay in milliseconds.</param>
    public AssetFileSystem(string assetRoot, bool autoStart = true, int flushDelayMs = 80)
    {
        if (string.IsNullOrWhiteSpace(assetRoot))
            throw new ArgumentException("Asset root is required.", nameof(assetRoot));

        this.assetRoot = Path.GetFullPath(assetRoot);
        Directory.CreateDirectory(this.assetRoot);

        m_pathKey = m_entries.DefineKey<string>("filesystem.path", PoolKeyFlags.Unique);
        m_parentPathKey = m_entries.DefineKey<string>("filesystem.parent");
        m_isDirectoryKey = m_entries.DefineKey<bool>("filesystem.dir");
        m_extensionKey = m_entries.DefineKey<string>("filesystem.ext");

        Refresh();

        m_watcher = new AssetWatcher(this.assetRoot, flushDelayMs);
        m_watcher.ChangedBatch += OnWatcherChangedBatch;
        if (autoStart)
            m_watcher.Start();
    }

    /// <summary>Starts source file watching.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_watcher.Start();
    }

    /// <summary>Stops source file watching.</summary>
    public void Stop()
    {
        if (m_disposed)
            return;

        m_watcher.Stop();
    }

    /// <summary>Rebuilds the indexed source file snapshot.</summary>
    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        lock (m_sync)
        {
            m_entries.RemoveAll();
            IndexDirectoryRecursive(assetRoot, string.Empty);
        }
    }

    /// <summary>Determines whether an indexed source entry exists.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    public bool Exists(string relativePath)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        string normalized = NormalizeRelativePath(relativePath);
        lock (m_sync)
        {
            return m_entries.First(m_pathKey, normalized) is not null;
        }
    }

    /// <summary>Tries to resolve an indexed source entry.</summary>
    /// <param name="relativePath">The source-relative path.</param>
    /// <param name="entry">The resolved entry when available.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    public bool TryGetEntry(string relativePath, out AssetFileEntry entry)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        string normalized = NormalizeRelativePath(relativePath);
        lock (m_sync)
        {
            AssetFileEntry? found = m_entries.First(m_pathKey, normalized);
            if (found is null)
            {
                entry = null!;
                return false;
            }

            entry = found;
            return true;
        }
    }

    /// <summary>Gets a stable snapshot of indexed entries.</summary>
    /// <param name="includeDirectories">Whether directory entries should be included.</param>
    /// <returns>The indexed entries.</returns>
    public IReadOnlyList<AssetFileEntry> GetEntries(bool includeDirectories = true)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        lock (m_sync)
        {
            IReadOnlyList<AssetFileEntry> all = includeDirectories
                ? m_entries.All()
                : m_entries.Find(m_isDirectoryKey, false);

            return all
                .OrderBy(static x => x.relativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>Gets immediate children of an indexed directory.</summary>
    /// <param name="parentRelativePath">The source-relative parent path.</param>
    /// <returns>The immediate child entries.</returns>
    public IReadOnlyList<AssetFileEntry> GetChildren(string parentRelativePath)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        string parent = NormalizeRelativePath(parentRelativePath);
        lock (m_sync)
        {
            IReadOnlyList<AssetFileEntry> children = m_entries
                .Find(m_parentPathKey, parent)
                .Where(x => !string.Equals(x.relativePath, parent, StringComparison.Ordinal))
                .ToArray();
            return children
                .OrderBy(static x => x.isDirectory ? 0 : 1)
                .ThenBy(static x => x.relativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>Waits until all queued watcher changes have been processed.</summary>
    public void WaitForIdle()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        if (!m_watcher.isWatching)
            return;

        m_watcher.WaitForIdle();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_watcher.ChangedBatch -= OnWatcherChangedBatch;
        m_watcher.Dispose();
    }

    private void OnWatcherChangedBatch(IReadOnlyList<AssetChangedEvent> changes)
    {
        if (m_disposed)
            return;

        try
        {
            Refresh();
        }
        catch (IOException)
        {
            // A rename can briefly expose an incomplete directory snapshot. The next watcher
            // batch or explicit refresh will reconcile it without terminating the host process.
            return;
        }
        catch (UnauthorizedAccessException)
        {
            // File permissions can change between enumeration and indexing.
            return;
        }
        ChangedBatch?.Invoke(changes);
    }

    private void IndexDirectoryRecursive(string absoluteDirectoryPath, string relativeDirectoryPath)
    {
        string normalizedDirectory = NormalizeRelativePath(relativeDirectoryPath);
        AddOrUpdateEntry(normalizedDirectory, isDirectory: true);

        foreach (string absoluteChildDirectory in Directory.EnumerateDirectories(absoluteDirectoryPath))
        {
            string name = Path.GetFileName(absoluteChildDirectory);
            string childRelativePath = CombineRelativePath(normalizedDirectory, name);
            IndexDirectoryRecursive(absoluteChildDirectory, childRelativePath);
        }

        foreach (string absoluteFile in Directory.EnumerateFiles(absoluteDirectoryPath))
        {
            string name = Path.GetFileName(absoluteFile);
            string fileRelativePath = CombineRelativePath(normalizedDirectory, name);
            AddOrUpdateEntry(fileRelativePath, isDirectory: false);
        }
    }

    private void AddOrUpdateEntry(string normalizedRelativePath, bool isDirectory)
    {
        string path = NormalizeRelativePath(normalizedRelativePath);
        AssetFileEntry? existing = m_entries.First(m_pathKey, path);
        if (existing is null)
        {
            existing = new AssetFileEntry();
            m_entries.Add(existing);
        }

        existing.relativePath = path;
        existing.parentRelativePath = GetParentPath(path);
        existing.isDirectory = isDirectory;
        existing.extension = isDirectory
            ? string.Empty
            : Path.GetExtension(path).ToLowerInvariant();

        m_entries.Add(existing)
            .Set(m_pathKey, existing.relativePath)
            .Set(m_parentPathKey, existing.parentRelativePath)
            .Set(m_isDirectoryKey, existing.isDirectory)
            .Set(m_extensionKey, existing.extension);
    }

    private static string GetParentPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;

        int lastSeparator = relativePath.LastIndexOf('/');
        if (lastSeparator < 0)
            return string.Empty;

        return relativePath[..lastSeparator];
    }

    private static string CombineRelativePath(string a, string b)
        => NormalizeRelativePath(Path.Combine(NormalizeRelativePath(a), NormalizeRelativePath(b)));

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;

        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("Asset file-system paths must be relative.", nameof(relativePath));

        string path = relativePath.Replace('\\', '/').Trim();
        while (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];
        while (path.StartsWith("/", StringComparison.Ordinal))
            path = path[1..];
        while (path.EndsWith("/", StringComparison.Ordinal))
            path = path[..^1];

        if (path == ".")
            return string.Empty;
        if (path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(static segment => segment == ".."))
        {
            throw new ArgumentException("Asset file-system paths cannot escape the configured root.", nameof(relativePath));
        }

        return path;
    }
}
