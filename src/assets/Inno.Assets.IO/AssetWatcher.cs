using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Inno.Assets.IO;

/// <summary>
/// File watcher that coalesces rapid file-system changes into batched callbacks.
/// </summary>
internal sealed class AssetWatcher : IDisposable
{
    private readonly string m_root;
    private readonly FileSystemWatcher m_watcher;
    private readonly Lock m_sync = new();
    private readonly Dictionary<string, AssetChangedEvent> m_pending = new(StringComparer.OrdinalIgnoreCase);
    private Timer? m_flushTimer;
    private readonly int m_flushDelayMs;

    /// <summary>
    /// Raised when pending changes are flushed as a batch.
    /// </summary>
    public event Action<IReadOnlyList<AssetChangedEvent>>? ChangedBatch;

    /// <summary>
    /// Creates a watcher for the provided root path.
    /// </summary>
    /// <param name="assetRoot">Absolute watched root directory.</param>
    /// <param name="flushDelayMs">Batching delay in milliseconds.</param>
    public AssetWatcher(string assetRoot, int flushDelayMs = 80)
    {
        m_root = assetRoot ?? throw new ArgumentNullException(nameof(assetRoot));
        m_flushDelayMs = Math.Max(flushDelayMs, 1);

        m_watcher = new FileSystemWatcher(assetRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
        };

        m_watcher.Changed += OnChanged;
        m_watcher.Created += OnChanged;
        m_watcher.Deleted += OnChanged;
        m_watcher.Renamed += OnRenamed;
    }

    /// <summary>
    /// Starts watching file-system changes.
    /// </summary>
    public void Start()
        => m_watcher.EnableRaisingEvents = true;

    /// <summary>
    /// Stops watching file-system changes.
    /// </summary>
    public void Stop()
        => m_watcher.EnableRaisingEvents = false;

    /// <summary>
    /// Stops watcher and releases resources.
    /// </summary>
    public void Dispose()
    {
        Stop();
        m_watcher.Changed -= OnChanged;
        m_watcher.Created -= OnChanged;
        m_watcher.Deleted -= OnChanged;
        m_watcher.Renamed -= OnRenamed;
        m_watcher.Dispose();
        lock (m_sync)
        {
            m_pending.Clear();
            m_flushTimer?.Dispose();
            m_flushTimer = null;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs args)
        => Enqueue(args.FullPath, args.ChangeType);

    private void OnRenamed(object sender, RenamedEventArgs args)
    {
        string oldRelative = NormalizeRelativePath(Path.GetRelativePath(m_root, args.OldFullPath));
        Enqueue(args.FullPath, WatcherChangeTypes.Renamed, oldRelative);
    }

    private void Enqueue(string fullPath, WatcherChangeTypes changeType, string oldRelativePath = "")
    {
        string relative = NormalizeRelativePath(Path.GetRelativePath(m_root, fullPath));

        lock (m_sync)
        {
            if (m_pending.TryGetValue(relative, out AssetChangedEvent existing))
            {
                WatcherChangeTypes mergedType = existing.changeType | changeType;
                string mergedOldRelativePath = !string.IsNullOrWhiteSpace(oldRelativePath)
                    ? oldRelativePath
                    : existing.oldRelativePath;
                m_pending[relative] = new AssetChangedEvent(relative, mergedType, mergedOldRelativePath);
            }
            else
            {
                m_pending[relative] = new AssetChangedEvent(relative, changeType, oldRelativePath);
            }

            m_flushTimer ??= new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            m_flushTimer.Change(m_flushDelayMs, Timeout.Infinite);
        }
    }

    private void Flush()
    {
        AssetChangedEvent[] batch;
        lock (m_sync)
        {
            if (m_pending.Count == 0)
                return;

            batch = new AssetChangedEvent[m_pending.Count];
            m_pending.Values.CopyTo(batch, 0);
            m_pending.Clear();
        }

        ChangedBatch?.Invoke(batch);
    }

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
