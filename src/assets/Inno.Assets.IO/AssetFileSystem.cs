using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using Inno.Core.Storage;

namespace Inno.Assets.IO;

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

    public string assetRoot { get; }

    /// <summary>
    /// Raised after batched source file-system changes are detected and index refresh completes.
    /// </summary>
    public event Action<IReadOnlyList<AssetChangedEvent>>? ChangedBatch;

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

    public void Start()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_watcher.Start();
    }

    public void Stop()
    {
        if (m_disposed)
            return;

        m_watcher.Stop();
    }

    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        lock (m_sync)
        {
            m_entries.RemoveAll();
            IndexDirectoryRecursive(assetRoot, string.Empty);
        }
    }

    public bool Exists(string relativePath)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        string normalized = NormalizeRelativePath(relativePath);
        lock (m_sync)
        {
            return m_entries.First(m_pathKey, normalized) is not null;
        }
    }

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

    public string BuildTreeGraph()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        AssetFileEntry[] snapshot;
        lock (m_sync)
        {
            snapshot = m_entries.All()
                .OrderBy(static x => x.relativePath.Count(static c => c == '/'))
                .ThenBy(static x => x.relativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var builder = new StringBuilder(4 * 1024);
        builder.AppendLine("Assets/");
        for (int i = 0; i < snapshot.Length; i++)
        {
            AssetFileEntry entry = snapshot[i];
            if (entry.relativePath.Length == 0)
                continue;

            int depth = entry.relativePath.Count(static c => c == '/') + 1;
            builder.Append(' ', depth * 2);
            builder.Append(entry.isDirectory ? "[D] " : "[F] ");
            builder.Append(Path.GetFileName(entry.relativePath));
            if (!entry.isDirectory && !string.IsNullOrEmpty(entry.extension))
            {
                builder.Append("  ");
                builder.Append(entry.extension);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

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

        Refresh();
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

        string path = relativePath.Replace('\\', '/').Trim();
        while (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];
        while (path.StartsWith("/", StringComparison.Ordinal))
            path = path[1..];
        while (path.EndsWith("/", StringComparison.Ordinal))
            path = path[..^1];

        return path == "." ? string.Empty : path;
    }
}
