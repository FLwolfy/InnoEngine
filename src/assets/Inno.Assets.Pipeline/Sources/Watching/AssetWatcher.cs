using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Collects physical source changes without invoking consumers on watcher threads.
/// </summary>
internal sealed class AssetWatcher : IDisposable
{
    private static readonly TimeSpan S_DEFAULT_IDLE_WAIT_TIMEOUT = TimeSpan.FromSeconds(10);

    private readonly string m_root;
    private readonly FileSystemWatcher m_watcher;
    private readonly Lock m_sync = new();
    private readonly List<AssetChangedEvent> m_pending = new(64);
    private readonly int m_flushDelayMs;

    private long m_lastEventTimestamp;
    private bool m_requiresFullRescan;
    private bool m_disposed;

    internal AssetWatcher(string assetRoot, int flushDelayMs)
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
        m_watcher.Error += OnError;
    }

    internal bool isWatching => m_watcher.EnableRaisingEvents;

    internal void Start()
    {
        ThrowIfDisposed();
        m_watcher.EnableRaisingEvents = true;
    }

    internal void Stop()
    {
        if (!m_disposed)
            m_watcher.EnableRaisingEvents = false;
    }

    internal WatcherPollResult Poll(bool force)
    {
        ThrowIfDisposed();
        AssetChangedEvent[] raw;
        bool requiresFullRescan;
        lock (m_sync)
        {
            if (!force && m_pending.Count > 0 && !HasQuietPeriodElapsed())
                return new WatcherPollResult(Array.Empty<AssetChangedEvent>(), false);
            raw = m_pending.ToArray();
            m_pending.Clear();
            requiresFullRescan = m_requiresFullRescan;
            m_requiresFullRescan = false;
        }
        AssetChangedEvent[] changes = AssetChangeBatchNormalizer.Normalize(m_root, raw);
        return new WatcherPollResult(changes, requiresFullRescan);
    }

    internal WatcherPollResult WaitForIdle()
    {
        ThrowIfDisposed();
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (true)
        {
            lock (m_sync)
            {
                if ((m_pending.Count > 0 || m_requiresFullRescan) && HasQuietPeriodElapsed())
                    return PollWhileLocked();
                if (m_pending.Count == 0 &&
                    !m_requiresFullRescan &&
                    stopwatch.ElapsedMilliseconds >= Math.Max(m_flushDelayMs * 2L, 50L))
                {
                    return new WatcherPollResult(Array.Empty<AssetChangedEvent>(), false);
                }
            }
            if (stopwatch.Elapsed > S_DEFAULT_IDLE_WAIT_TIMEOUT)
                throw new TimeoutException("Timed out waiting for AssetWatcher to become idle.");
            Thread.Sleep(Math.Min(m_flushDelayMs, 20));
        }
    }

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_watcher.EnableRaisingEvents = false;
        m_watcher.Changed -= OnChanged;
        m_watcher.Created -= OnChanged;
        m_watcher.Deleted -= OnChanged;
        m_watcher.Renamed -= OnRenamed;
        m_watcher.Error -= OnError;
        m_watcher.Dispose();
        lock (m_sync)
        {
            m_pending.Clear();
            m_requiresFullRescan = false;
        }
    }

    private WatcherPollResult PollWhileLocked()
    {
        AssetChangedEvent[] raw = m_pending.ToArray();
        m_pending.Clear();
        bool requiresFullRescan = m_requiresFullRescan;
        m_requiresFullRescan = false;
        return new WatcherPollResult(
            AssetChangeBatchNormalizer.Normalize(m_root, raw),
            requiresFullRescan);
    }

    private bool HasQuietPeriodElapsed()
    {
        long elapsed = Stopwatch.GetTimestamp() - m_lastEventTimestamp;
        return elapsed >= (long)(Stopwatch.Frequency * (m_flushDelayMs / 1000d));
    }

    private void OnChanged(object sender, FileSystemEventArgs args)
        => Enqueue(args.FullPath, args.ChangeType);

    private void OnRenamed(object sender, RenamedEventArgs args)
    {
        string oldRelative = NormalizeRelativePath(Path.GetRelativePath(m_root, args.OldFullPath));
        Enqueue(args.FullPath, WatcherChangeTypes.Renamed, oldRelative);
    }

    private void OnError(object sender, ErrorEventArgs args)
    {
        lock (m_sync)
        {
            if (m_disposed)
                return;
            m_requiresFullRescan = true;
            m_lastEventTimestamp = Stopwatch.GetTimestamp();
        }
    }

    private void Enqueue(string fullPath, WatcherChangeTypes changeType, string oldRelativePath = "")
    {
        if (m_disposed)
            return;
        string relativePath = NormalizeRelativePath(Path.GetRelativePath(m_root, fullPath));
        if (AssetSourcePolicy.IsGeneratedPath(relativePath))
            return;
        lock (m_sync)
        {
            if (m_disposed)
                return;
            m_pending.Add(new AssetChangedEvent(relativePath, changeType, oldRelativePath));
            m_lastEventTimestamp = Stopwatch.GetTimestamp();
        }
    }

    private void ThrowIfDisposed()
    {
        if (m_disposed)
            throw new ObjectDisposedException(nameof(AssetWatcher));
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        string path = relativePath.Replace('\\', '/').Trim('/');
        return path == "." ? string.Empty : path;
    }
}

internal readonly struct WatcherPollResult(
    IReadOnlyList<AssetChangedEvent> changes,
    bool requiresFullRescan)
{
    internal IReadOnlyList<AssetChangedEvent> changes { get; } = changes ?? Array.Empty<AssetChangedEvent>();
    internal bool requiresFullRescan { get; } = requiresFullRescan;
}
