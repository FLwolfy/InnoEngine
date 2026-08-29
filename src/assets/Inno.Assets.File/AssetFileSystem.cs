using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Inno.Assets.Core;
using Inno.Core.Storage;

namespace Inno.Assets.File;

/// <summary>
/// Indexed asset source filesystem model backed by <see cref="AssetWatcher"/>.
/// </summary>
public sealed class AssetFileSystem : IDisposable
{
    private readonly IndexedObjectStore<AssetFileEntry> m_entries = new();
    private readonly IndexedObjectKey<string> m_pathKey;
    private readonly IndexedObjectKey<string> m_parentPathKey;
    private readonly IndexedObjectKey<bool> m_isDirectoryKey;
    private readonly IndexedObjectKey<string> m_extensionKey;
    private readonly Lock m_sync = new();
    private readonly AssetWatcher m_watcher;
    private readonly AssetSourcePolicy m_sourcePolicy;
    private readonly IReadOnlyDictionary<AssetSourceId, AssetSourceMount> m_mounts;
    private bool m_disposed;

    /// <summary>Gets the absolute source asset root.</summary>
    public string assetRoot { get; }
    
    /// <summary>Gets whether source file watching is active.</summary>
    public bool isWatching => m_watcher.isWatching;

    /// <summary>Creates an indexed source file system.</summary>
    /// <param name="assetRoot">The absolute source root.</param>
    /// <param name="autoStart">Whether file watching should start immediately.</param>
    /// <param name="flushDelayMs">The watcher batch delay in milliseconds.</param>
    /// <param name="sourcePolicy">The source filtering policy, or <see langword="null"/> for defaults.</param>
    public AssetFileSystem(
        string assetRoot,
        bool autoStart = true,
        int flushDelayMs = 80,
        AssetSourcePolicy? sourcePolicy = null)
        : this(
            [new AssetSourceMount(AssetSourceId.project, assetRoot, isReadOnly: false)],
            autoStart,
            flushDelayMs,
            sourcePolicy)
    {
    }

    /// <summary>Creates an indexed file system over isolated source mounts.</summary>
    /// <param name="mounts">Complete source mount snapshot.</param>
    /// <param name="autoStart">Whether writable project source watching starts immediately.</param>
    /// <param name="flushDelayMs">Watcher batch delay in milliseconds.</param>
    /// <param name="sourcePolicy">Source filtering policy, or <see langword="null"/> for defaults.</param>
    public AssetFileSystem(
        IReadOnlyList<AssetSourceMount> mounts,
        bool autoStart = true,
        int flushDelayMs = 80,
        AssetSourcePolicy? sourcePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(mounts);
        if (mounts.Count == 0)
            throw new ArgumentException("At least one asset source mount is required.", nameof(mounts));
        Dictionary<AssetSourceId, AssetSourceMount> byId = mounts.ToDictionary(static mount => mount.id);
        if (byId.Count != mounts.Count)
            throw new ArgumentException("Asset source mount IDs must be unique.", nameof(mounts));
        if (!byId.TryGetValue(AssetSourceId.project, out AssetSourceMount? project)
            || project.isReadOnly)
        {
            throw new ArgumentException("A writable project asset source mount is required.", nameof(mounts));
        }

        assetRoot = project.rootPath;
        m_mounts = byId;
        foreach (AssetSourceMount mount in mounts)
            Directory.CreateDirectory(mount.rootPath);
        m_sourcePolicy = sourcePolicy ?? AssetSourcePolicy.defaultPolicy;

        m_pathKey = m_entries.DefineKey<string>("filesystem.path", IndexedObjectKeyFlags.Unique);
        m_parentPathKey = m_entries.DefineKey<string>("filesystem.parent");
        m_isDirectoryKey = m_entries.DefineKey<bool>("filesystem.dir");
        m_extensionKey = m_entries.DefineKey<string>("filesystem.ext");

        Refresh();

        m_watcher = new AssetWatcher(assetRoot, flushDelayMs);
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
            foreach (AssetSourceMount mount in m_mounts.Values.OrderBy(static value => value.id.value, StringComparer.Ordinal))
                IndexDirectoryRecursive(mount, mount.rootPath, string.Empty);
        }
    }

    /// <summary>Determines whether an indexed source entry exists.</summary>
    /// <param name="path">The isolated source path.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    public bool Exists(AssetPath path)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        string normalized = NormalizeAssetPath(path);
        lock (m_sync)
        {
            return m_entries.First(m_pathKey, normalized) is not null;
        }
    }

    /// <summary>Tries to resolve an indexed source entry.</summary>
    /// <param name="path">The isolated source path.</param>
    /// <param name="entry">The resolved entry when available.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    public bool TryGetEntry(AssetPath path, out AssetFileEntry entry)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        string normalized = NormalizeAssetPath(path);
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
                .OrderBy(static x => x.assetPath.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>Gets immediate children of an indexed directory.</summary>
    /// <param name="parent">The isolated parent directory path.</param>
    /// <returns>The immediate child entries.</returns>
    public IReadOnlyList<AssetFileEntry> GetChildren(AssetPath parent)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        string normalizedParent = NormalizeAssetPath(parent);
        lock (m_sync)
        {
            IReadOnlyList<AssetFileEntry> children = m_entries
                .Find(m_parentPathKey, normalizedParent)
                .Where(x => !string.Equals(x.assetPath.ToString(), normalizedParent, StringComparison.Ordinal))
                .ToArray();
            return children
                .OrderBy(static x => x.isDirectory ? 0 : 1)
                .ThenBy(static x => x.assetPath.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>Polls normalized changes and refreshes the indexed source snapshot.</summary>
    /// <returns>The changes observed since the previous poll.</returns>
    public IReadOnlyList<AssetChangedEvent> PollChanges()
        => PollChanges(out _);

    /// <summary>Polls changes and reports whether watcher recovery requires a full rescan.</summary>
    /// <param name="requiresFullRescan">Whether the watcher reported an unreliable event stream.</param>
    /// <returns>The changes observed since the previous poll.</returns>
    public IReadOnlyList<AssetChangedEvent> PollChanges(out bool requiresFullRescan)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        WatcherPollResult result = m_watcher.Poll(force: false);
        requiresFullRescan = result.requiresFullRescan;
        if (result.changes.Count > 0 || requiresFullRescan)
            RefreshSafely();
        return result.changes;
    }

    /// <summary>Waits for a quiet watcher window, refreshes the index, and returns queued changes.</summary>
    public IReadOnlyList<AssetChangedEvent> WaitForIdle()
        => WaitForIdle(out _);

    /// <summary>Waits for queued changes and reports whether a full rescan is required.</summary>
    /// <param name="requiresFullRescan">Whether the watcher reported an unreliable event stream.</param>
    /// <returns>The normalized queued changes.</returns>
    public IReadOnlyList<AssetChangedEvent> WaitForIdle(out bool requiresFullRescan)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        if (!m_watcher.isWatching)
        {
            requiresFullRescan = false;
            return Array.Empty<AssetChangedEvent>();
        }

        WatcherPollResult result = m_watcher.WaitForIdle();
        requiresFullRescan = result.requiresFullRescan;
        if (result.changes.Count > 0 || requiresFullRescan)
            RefreshSafely();
        return result.changes;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_watcher.Dispose();
    }

    private void RefreshSafely()
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
    }

    private void IndexDirectoryRecursive(
        AssetSourceMount mount,
        string absoluteDirectoryPath,
        string localDirectoryPath)
    {
        string normalizedDirectory = NormalizeLocalPath(localDirectoryPath);
        AddOrUpdateEntry(new AssetPath(mount.id, normalizedDirectory), mount.isReadOnly, isDirectory: true);

        foreach (string absoluteChildDirectory in Directory.EnumerateDirectories(absoluteDirectoryPath))
        {
            string name = Path.GetFileName(absoluteChildDirectory);
            string childRelativePath = CombineLocalPath(normalizedDirectory, name);
            if (m_sourcePolicy.IsIgnored(childRelativePath, isDirectory: true))
                continue;
            IndexDirectoryRecursive(mount, absoluteChildDirectory, childRelativePath);
        }

        foreach (string absoluteFile in Directory.EnumerateFiles(absoluteDirectoryPath))
        {
            string name = Path.GetFileName(absoluteFile);
            string fileRelativePath = CombineLocalPath(normalizedDirectory, name);
            if (m_sourcePolicy.IsIgnored(fileRelativePath, isDirectory: false))
                continue;
            AddOrUpdateEntry(new AssetPath(mount.id, fileRelativePath), mount.isReadOnly, isDirectory: false);
        }
    }

    private void AddOrUpdateEntry(AssetPath assetPath, bool isReadOnly, bool isDirectory)
    {
        string path = assetPath.ToString();
        AssetFileEntry? existing = m_entries.First(m_pathKey, path);
        if (existing is null)
        {
            existing = new AssetFileEntry();
            m_entries.Add(existing);
        }

        existing.assetPath = assetPath;
        existing.isReadOnly = isReadOnly;
        existing.parentAssetPath = AssetPath.Parse(GetParentPath(path));
        existing.isDirectory = isDirectory;
        existing.extension = isDirectory
            ? string.Empty
            : Path.GetExtension(path).ToLowerInvariant();

        m_entries.Add(existing)
            .Set(m_pathKey, existing.assetPath.ToString())
            .Set(m_parentPathKey, existing.parentAssetPath.ToString())
            .Set(m_isDirectoryKey, existing.isDirectory)
            .Set(m_extensionKey, existing.extension);
    }

    private static string GetParentPath(string relativePath)
    {
        AssetPath path = AssetPath.Parse(relativePath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(path.localPath))
            return string.Empty;

        int lastSeparator = path.localPath.LastIndexOf('/');
        string parent = lastSeparator < 0 ? string.Empty : path.localPath[..lastSeparator];

        return new AssetPath(path.source, parent).ToString();
    }

    private static string CombineLocalPath(string a, string b)
        => NormalizeLocalPath(Path.Combine(NormalizeLocalPath(a), NormalizeLocalPath(b)));

    private string NormalizeAssetPath(AssetPath path)
    {
        if (!path.isValid)
            throw new ArgumentException("An isolated asset path is required.", nameof(path));
        if (!m_mounts.ContainsKey(path.source))
            throw new ArgumentException($"Asset source mount '{path.source}' is not indexed.", nameof(path));
        return path.ToString();
    }

    private static string NormalizeLocalPath(string relativePath)
        => new AssetPath(AssetSourceId.project, relativePath ?? string.Empty).localPath;
}
