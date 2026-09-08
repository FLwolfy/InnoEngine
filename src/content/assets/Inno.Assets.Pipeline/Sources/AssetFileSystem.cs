using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

using Inno.Assets;
using Inno.Core.Identity;
using Inno.Core.Collections;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Indexed asset source filesystem model backed by <see cref="AssetWatcher"/>.
/// </summary>
public sealed class AssetFileSystem : IDisposable
{
    private readonly IndexedObjectStore<AssetFileEntry> m_entries = new();
    private readonly IdentityAllocator m_identities;
    private readonly IndexedObjectKey<string> m_pathKey;
    private readonly IndexedObjectKey<string> m_parentPathKey;
    private readonly IndexedObjectKey<bool> m_isDirectoryKey;
    private readonly IndexedObjectKey<string> m_extensionKey;
    private readonly Lock m_sync = new();
    private readonly AssetWatcher m_watcher;
    private readonly AssetSourcePolicy m_sourcePolicy;
    private readonly IReadOnlyDictionary<AssetSourceId, AssetSourceMount> m_mounts;
    private readonly Func<AssetPath, Guid?>? m_persistentIdentityResolver;
    private bool m_identitiesActive;
    private bool m_disposed;

    /// <summary>
    /// Gets the absolute source asset root.
    /// </summary>
    public string assetRoot { get; }
    
    /// <summary>
    /// Gets whether source file watching is active.
    /// </summary>
    public bool isWatching => m_watcher.isWatching;

    /// <summary>
    /// Creates an indexed source file system.
    /// </summary>
    /// <param name="assetRoot">
    /// The absolute source root.
    /// </param>
    /// <param name="autoStart">
    /// Whether file watching should start immediately.
    /// </param>
    /// <param name="flushDelayMs">
    /// The watcher batch delay in milliseconds.
    /// </param>
    /// <param name="sourcePolicy">
    /// The source filtering policy, or <see langword="null"/> for defaults.
    /// </param>
    public AssetFileSystem(
        string assetRoot,
        bool autoStart = true,
        int flushDelayMs = 80,
        AssetSourcePolicy? sourcePolicy = null)
        : this(
            [new AssetSourceMount(AssetSourceId.project, assetRoot, isReadOnly: false)],
            autoStart,
            flushDelayMs,
            sourcePolicy,
            requireWritableProject: true,
            identities: new IdentityAllocator(),
            persistentIdentityResolver: null,
            activateIdentities: true)
    {
    }

    /// <summary>
    /// Creates an indexed file system over isolated source mounts.
    /// </summary>
    /// <param name="mounts">
    /// Complete source mount snapshot.
    /// </param>
    /// <param name="autoStart">
    /// Whether writable project source watching starts immediately.
    /// </param>
    /// <param name="flushDelayMs">
    /// Watcher batch delay in milliseconds.
    /// </param>
    /// <param name="sourcePolicy">
    /// Source filtering policy, or <see langword="null"/> for defaults.
    /// </param>
    /// <param name="requireWritableProject">
    /// Whether the project mount must support authoring mutations. Runtime catalogs may set this to
    /// <see langword="false"/> when every mounted identity directory is read-only.
    /// </param>
    public AssetFileSystem(
        IReadOnlyList<AssetSourceMount> mounts,
        bool autoStart = true,
        int flushDelayMs = 80,
        AssetSourcePolicy? sourcePolicy = null,
        bool requireWritableProject = true)
        : this(
            mounts,
            autoStart,
            flushDelayMs,
            sourcePolicy,
            requireWritableProject,
            identities: new IdentityAllocator(),
            persistentIdentityResolver: null,
            activateIdentities: true)
    {
    }

    internal AssetFileSystem(
        IReadOnlyList<AssetSourceMount> mounts,
        bool autoStart,
        int flushDelayMs,
        AssetSourcePolicy? sourcePolicy,
        bool requireWritableProject,
        IdentityAllocator identities,
        Func<AssetPath, Guid?>? persistentIdentityResolver,
        bool activateIdentities)
    {
        ArgumentNullException.ThrowIfNull(mounts);
        ArgumentNullException.ThrowIfNull(identities);
        if (mounts.Count == 0)
            throw new ArgumentException("At least one asset source mount is required.", nameof(mounts));
        Dictionary<AssetSourceId, AssetSourceMount> byId = mounts.ToDictionary(static mount => mount.id);
        if (byId.Count != mounts.Count)
            throw new ArgumentException("Asset source mount IDs must be unique.", nameof(mounts));
        if (!byId.TryGetValue(AssetSourceId.project, out AssetSourceMount? project)
            || project.isReadOnly && requireWritableProject)
        {
            throw new ArgumentException(
                requireWritableProject
                    ? "A writable project asset source mount is required."
                    : "A project asset source mount is required.",
                nameof(mounts));
        }
        if (project.isReadOnly && autoStart)
            throw new ArgumentException("A read-only project source cannot be watched for authoring changes.", nameof(autoStart));

        assetRoot = project.rootPath;
        m_identities = identities;
        m_mounts = byId;
        m_persistentIdentityResolver = persistentIdentityResolver;
        m_identitiesActive = activateIdentities;
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

    /// <summary>
    /// Starts source file watching.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_watcher.Start();
    }

    /// <summary>
    /// Stops source file watching.
    /// </summary>
    public void Stop()
    {
        if (m_disposed)
            return;

        m_watcher.Stop();
    }

    /// <summary>
    /// Rebuilds the indexed source file snapshot.
    /// </summary>
    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        lock (m_sync)
        {
            UnregisterEntries();
            m_entries.RemoveAll();
            foreach (AssetSourceMount mount in m_mounts.Values.OrderBy(static value => value.id.value, StringComparer.Ordinal))
                IndexDirectoryRecursive(mount, mount.rootPath, string.Empty);
        }
    }

    /// <summary>
    /// Determines whether an indexed source entry exists.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the entry exists.
    /// </returns>
    public bool Exists(AssetPath path)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        string normalized = NormalizeAssetPath(path);
        lock (m_sync)
        {
            return m_entries.First(m_pathKey, normalized) is not null;
        }
    }

    /// <summary>
    /// Tries to resolve an indexed source entry.
    /// </summary>
    /// <param name="path">
    /// The isolated source path.
    /// </param>
    /// <param name="entry">
    /// The resolved entry when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the entry exists.
    /// </returns>
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

    /// <summary>
    /// Gets a stable snapshot of indexed entries.
    /// </summary>
    /// <param name="includeDirectories">
    /// Whether directory entries should be included.
    /// </param>
    /// <returns>
    /// The indexed entries.
    /// </returns>
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

    /// <summary>
    /// Gets immediate children of an indexed directory.
    /// </summary>
    /// <param name="parent">
    /// The isolated parent directory path.
    /// </param>
    /// <returns>
    /// The immediate child entries.
    /// </returns>
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

    /// <summary>
    /// Polls normalized changes and refreshes the indexed source snapshot.
    /// </summary>
    /// <returns>
    /// The changes observed since the previous poll.
    /// </returns>
    public IReadOnlyList<AssetChangedEvent> PollChanges()
        => PollChanges(out _);

    /// <summary>
    /// Polls changes and reports whether watcher recovery requires a full rescan.
    /// </summary>
    /// <param name="requiresFullRescan">
    /// Whether the watcher reported an unreliable event stream.
    /// </param>
    /// <returns>
    /// The changes observed since the previous poll.
    /// </returns>
    public IReadOnlyList<AssetChangedEvent> PollChanges(out bool requiresFullRescan)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        WatcherPollResult result = m_watcher.Poll(force: false);
        requiresFullRescan = result.requiresFullRescan;
        if (result.changes.Count > 0 || requiresFullRescan)
            RefreshSafely();
        return result.changes;
    }

    /// <summary>
    /// Waits for a quiet watcher window, refreshes the index, and returns queued changes.
    /// </summary>
    /// <returns>
    /// An immutable snapshot of the values selected by the operation.
    /// </returns>
    public IReadOnlyList<AssetChangedEvent> WaitForIdle()
        => WaitForIdle(out _);

    /// <summary>
    /// Waits for queued changes and reports whether a full rescan is required.
    /// </summary>
    /// <param name="requiresFullRescan">
    /// Whether the watcher reported an unreliable event stream.
    /// </param>
    /// <returns>
    /// The normalized queued changes.
    /// </returns>
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

    /// <summary>
    /// Releases the resources owned by this implementation.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_watcher.Dispose();
        lock (m_sync)
        {
            DeactivateIdentitiesLocked();
            m_entries.RemoveAll();
        }
    }

    internal void ActivateIdentities()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        lock (m_sync)
        {
            if (m_identitiesActive)
                return;
            var registered = new List<AssetFileEntry>();
            try
            {
                foreach (AssetFileEntry entry in m_entries.All())
                {
                    if (!m_identities.Register(entry, entry.identity.persistentId))
                    {
                        throw new InvalidOperationException(
                            $"Asset source entry '{entry.assetPath}' was already registered.");
                    }
                    registered.Add(entry);
                }
                m_identitiesActive = true;
            }
            catch
            {
                foreach (AssetFileEntry entry in registered)
                    _ = m_identities.Unregister(entry);
                throw;
            }
        }
    }

    internal void DeactivateIdentities()
    {
        lock (m_sync)
            DeactivateIdentitiesLocked();
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
            Guid sourcePersistentId = m_persistentIdentityResolver?.Invoke(assetPath)
                ?? CreateFallbackPersistentId(path);
            Guid entryPersistentId = CreateEntryPersistentId(sourcePersistentId);
            if (m_identitiesActive)
                _ = m_identities.Register(existing, entryPersistentId);
            else
                m_identities.InitializePersistentIdentity(existing, entryPersistentId);
        }

        existing.assetPath = assetPath;
        existing.isReadOnly = isReadOnly;
        existing.isSample = isDirectory && AssetSample.IsRoot(assetPath);
        existing.isSampleContent = AssetSample.Contains(assetPath, isDirectory);
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

    private static Guid CreateFallbackPersistentId(string path)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        Span<byte> namespaceBytes = stackalloc byte[16];
        new Guid("eac730be-e359-49f7-86c9-b3b09da457cc").TryWriteBytes(namespaceBytes);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(namespaceBytes);
        hash.AppendData(pathBytes);
        return new Guid(hash.GetHashAndReset().AsSpan(0, 16));
    }

    private static Guid CreateEntryPersistentId(Guid sourcePersistentId)
    {
        Span<byte> input = stackalloc byte[32];
        sourcePersistentId.TryWriteBytes(input[..16]);
        new Guid("f42622a8-893d-4d5d-b1c0-30910cf5378e").TryWriteBytes(input[16..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }

    private void UnregisterEntries()
    {
        foreach (AssetFileEntry entry in m_entries.All())
            _ = m_identities.Unregister(entry);
    }

    private void DeactivateIdentitiesLocked()
    {
        if (!m_identitiesActive)
            return;
        UnregisterEntries();
        m_identitiesActive = false;
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
