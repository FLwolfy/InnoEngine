using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Inno.Core.Logging;

namespace Inno.Assets.Core;

public enum AssetDirectoryChangeKind
{
    Created,
    Changed,
    Deleted,
    Renamed
}

public readonly struct AssetDirectoryChange
{
    public readonly AssetDirectoryChangeKind kind;
    public readonly string fullPath;
    public readonly string relativePath;
    public readonly string? oldFullPath;
    public readonly string? oldRelativePath;

    public AssetDirectoryChange(
        AssetDirectoryChangeKind kind,
        string fullPath,
        string relativePath,
        string? oldFullPath = null,
        string? oldRelativePath = null)
    {
        this.kind = kind;
        this.fullPath = fullPath;
        this.relativePath = relativePath;
        this.oldFullPath = oldFullPath;
        this.oldRelativePath = oldRelativePath;
    }
}

public delegate void AssetDirectoryChangedHandler(in AssetDirectoryChange change);
public delegate void AssetDirectoryChangesFlushedHandler(IReadOnlyList<AssetDirectoryChange> changes);

/// <summary>
/// Observes the asset directory (watcher + coalescing) and provides asset-aware file operations.
/// </summary>
internal sealed class AssetFileSystem : IDisposable
{
    #region Events

    public event AssetDirectoryChangedHandler? AssetDirectoryChanged;
    public event AssetDirectoryChangesFlushedHandler? AssetDirectoryChangesFlushed;

    #endregion

    #region Roots

    public string rootDirectory { get; }
    public string? binRootDirectory { get; }

    #endregion

    #region Watcher

    private readonly FileSystemWatcher m_watcher;

    private int m_changeVersion;
    public int changeVersion => Volatile.Read(ref m_changeVersion);

    private readonly int m_flushDelayMs;

    private readonly object m_changeSync = new();
    private readonly Dictionary<string, AssetDirectoryChange> m_pending = new(StringComparer.OrdinalIgnoreCase);
    private Timer? m_flushTimer;
    private bool m_flushScheduled;

    /// <summary>
    /// Creates a watcher rooted at <paramref name="assetDirectory"/> and a bin mirror rooted at <paramref name="binDirectory"/>.
    /// </summary>
    /// <param name="assetDirectory">Absolute asset directory path.</param>
    /// <param name="binDirectory">Absolute bin directory path.</param>
    /// <param name="flushDelayMs">Coalescing delay.</param>
    /// <param name="internalBufferSizeBytes">Watcher internal buffer size.</param>
    public AssetFileSystem(
        string assetDirectory,
        string binDirectory,
        int flushDelayMs = 120,
        int internalBufferSizeBytes = 64 * 1024)
    {
        if (string.IsNullOrWhiteSpace(assetDirectory)) throw new ArgumentException("assetDirectory is null/empty.");
        if (!Directory.Exists(assetDirectory)) throw new DirectoryNotFoundException(assetDirectory);

        rootDirectory = assetDirectory;
        binRootDirectory = binDirectory;

        m_flushDelayMs = Math.Max(1, flushDelayMs);

        m_watcher = new FileSystemWatcher(assetDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = Math.Clamp(internalBufferSizeBytes, 4 * 1024, 256 * 1024)
        };

        m_watcher.Created += OnCreated;
        m_watcher.Changed += OnChanged;
        m_watcher.Deleted += OnDeleted;
        m_watcher.Renamed += OnRenamed;

        m_watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Stops watching and releases resources.
    /// </summary>
    public void Dispose()
    {
        m_watcher.EnableRaisingEvents = false;

        m_watcher.Created -= OnCreated;
        m_watcher.Changed -= OnChanged;
        m_watcher.Deleted -= OnDeleted;
        m_watcher.Renamed -= OnRenamed;

        m_watcher.Dispose();

        lock (m_changeSync)
        {
            m_flushTimer?.Dispose();
            m_flushTimer = null;
            m_pending.Clear();
            m_flushScheduled = false;
        }
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
        => Enqueue(new AssetDirectoryChange(
            AssetDirectoryChangeKind.Created,
            e.FullPath,
            NormalizeRelativePath(Path.GetRelativePath(rootDirectory, e.FullPath))));

    private void OnChanged(object sender, FileSystemEventArgs e)
        => Enqueue(new AssetDirectoryChange(
            AssetDirectoryChangeKind.Changed,
            e.FullPath,
            NormalizeRelativePath(Path.GetRelativePath(rootDirectory, e.FullPath))));

    private void OnDeleted(object sender, FileSystemEventArgs e)
        => Enqueue(new AssetDirectoryChange(
            AssetDirectoryChangeKind.Deleted,
            e.FullPath,
            NormalizeRelativePath(Path.GetRelativePath(rootDirectory, e.FullPath))));

    private void OnRenamed(object sender, RenamedEventArgs e)
        => Enqueue(new AssetDirectoryChange(
            AssetDirectoryChangeKind.Renamed,
            e.FullPath,
            Path.GetRelativePath(rootDirectory, e.FullPath),
            oldFullPath: e.OldFullPath,
            oldRelativePath: NormalizeRelativePath(Path.GetRelativePath(rootDirectory, e.OldFullPath))));

    private void Enqueue(in AssetDirectoryChange change)
    {
        lock (m_changeSync)
        {
            MergePending(change);

            if (!m_flushScheduled)
            {
                m_flushScheduled = true;
                m_flushTimer ??= new Timer(_ => Flush(), null, m_flushDelayMs, Timeout.Infinite);
                m_flushTimer.Change(m_flushDelayMs, Timeout.Infinite);
            }
            else
            {
                m_flushTimer?.Change(m_flushDelayMs, Timeout.Infinite);
            }
        }
    }

    private void MergePending(in AssetDirectoryChange change)
    {
        string key = change.relativePath;

        if (change.kind == AssetDirectoryChangeKind.Renamed && !string.IsNullOrEmpty(change.oldRelativePath))
            m_pending.Remove(change.oldRelativePath!);

        if (!m_pending.TryGetValue(key, out var existing))
        {
            m_pending[key] = change;
            return;
        }

        if (change.kind == AssetDirectoryChangeKind.Renamed)
        {
            m_pending[key] = change;
            return;
        }

        if (change.kind == AssetDirectoryChangeKind.Deleted)
        {
            m_pending[key] = change;
            return;
        }

        if (existing.kind == AssetDirectoryChangeKind.Deleted) return;
        if (existing.kind == AssetDirectoryChangeKind.Renamed) return;
        if (existing.kind == AssetDirectoryChangeKind.Created) return;

        m_pending[key] = change;
    }

    private void Flush()
    {
        AssetDirectoryChange[] changes;

        lock (m_changeSync)
        {
            m_flushScheduled = false;

            if (m_pending.Count == 0) return;

            changes = m_pending.Values.ToArray();
            m_pending.Clear();
        }

        Interlocked.Increment(ref m_changeVersion);

        if (AssetDirectoryChanged != null)
        {
            for (int i = 0; i < changes.Length; i++)
                AssetDirectoryChanged.Invoke(changes[i]);
        }

        AssetDirectoryChangesFlushed?.Invoke(changes);
    }

    #endregion

    #region Asset-aware file operations

    /// <summary>
    /// Creates a directory under the asset root (also creates parents).
    /// </summary>
    /// <param name="relativeDirectory">Path relative to asset root.</param>
    /// <returns>True if created (or already exists); otherwise false.</returns>
    public bool CreateDirectory(string relativeDirectory)
    {
        relativeDirectory = NormalizeRelativePath(relativeDirectory);

        try
        {
            Directory.CreateDirectory(AbsAsset(relativeDirectory));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a file or directory under the asset root, including .asset meta and .bin mirror content.
    /// </summary>
    /// <param name="relativePath">Path relative to asset root.</param>
    /// <returns>True if deleted; otherwise false.</returns>
    public bool DeletePath(string relativePath)
    {
        relativePath = NormalizeRelativePath(relativePath);

        try
        {
            string abs = AbsAsset(relativePath);

            if (Directory.Exists(abs))
                return DeleteDirectory(relativePath);

            if (File.Exists(abs))
                return DeleteFile(relativePath);

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a single file and its associated meta/bin files.
    /// </summary>
    /// <param name="relativeFilePath">File path relative to asset root.</param>
    /// <returns>True if deleted; otherwise false.</returns>
    public bool DeleteFile(string relativeFilePath)
    {
        relativeFilePath = NormalizeRelativePath(relativeFilePath);

        try
        {
            SafeDeleteFile(AbsAsset(relativeFilePath));
            SafeDeleteFile(AbsMeta(relativeFilePath));
            SafeDeleteFile(AbsBin(relativeFilePath));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a directory recursively under the asset root, including the mirrored bin directory.
    /// </summary>
    /// <param name="relativeDirectory">Directory path relative to asset root.</param>
    /// <returns>True if deleted; otherwise false.</returns>
    public bool DeleteDirectory(string relativeDirectory)
    {
        relativeDirectory = NormalizeRelativePath(relativeDirectory);

        try
        {
            string absAssetDir = AbsAsset(relativeDirectory);
            if (Directory.Exists(absAssetDir))
            {
                SafeDeleteDirectoryRecursive(absAssetDir);
                SafeDeleteDirectoryWithRetry(absAssetDir);
            }

            string absBinDir = AbsBinDir(relativeDirectory);
            if (Directory.Exists(absBinDir))
            {
                SafeDeleteDirectoryRecursive(absBinDir);
                SafeDeleteDirectoryWithRetry(absBinDir);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Renames/moves a file or directory under the asset root, including meta/bin and meta sourcePath rewrites.
    /// </summary>
    /// <param name="oldRelativePath">Old path relative to asset root.</param>
    /// <param name="newRelativePath">New path relative to asset root.</param>
    /// <returns>True if renamed; otherwise false.</returns>
    public bool RenamePath(string oldRelativePath, string newRelativePath)
    {
        oldRelativePath = NormalizeRelativePath(oldRelativePath);
        newRelativePath = NormalizeRelativePath(newRelativePath);

        try
        {
            string absOld = AbsAsset(oldRelativePath);
            string absNew = AbsAsset(newRelativePath);

            if (Directory.Exists(absOld))
            {
                SafeEnsureParentDirectory(absNew);
                if (Directory.Exists(absNew)) return false;

                Directory.Move(absOld, absNew);

                string absOldBinDir = AbsBinDir(oldRelativePath);
                string absNewBinDir = AbsBinDir(newRelativePath);

                if (Directory.Exists(absOldBinDir))
                {
                    SafeEnsureParentDirectory(absNewBinDir);

                    if (Directory.Exists(absNewBinDir))
                        SafeDeleteDirectoryRecursive(absNewBinDir);

                    Directory.Move(absOldBinDir, absNewBinDir);
                }

                UpdateMetaSourcePathPrefix(absNew, oldRelativePath, newRelativePath);
                return true;
            }

            if (File.Exists(absOld))
            {
                SafeEnsureParentDirectory(absNew);
                if (File.Exists(absNew)) return false;

                File.Move(absOld, absNew);

                string absOldMeta = AbsMeta(oldRelativePath);
                string absNewMeta = AbsMeta(newRelativePath);
                if (File.Exists(absOldMeta))
                {
                    SafeEnsureParentDirectory(absNewMeta);
                    File.Move(absOldMeta, absNewMeta);
                    UpdateMetaSourcePathExact(absNewMeta, oldRelativePath, newRelativePath);
                }

                string absOldBin = AbsBin(oldRelativePath);
                string absNewBin = AbsBin(newRelativePath);
                if (File.Exists(absOldBin))
                {
                    SafeEnsureParentDirectory(absNewBin);
                    File.Move(absOldBin, absNewBin);
                }

                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Moves a file or directory into an existing destination directory, preserving the source name.
    /// </summary>
    /// <param name="sourceRelativePath">Source path relative to asset root.</param>
    /// <param name="destinationRelativeDirectory">Destination directory relative to asset root.</param>
    /// <returns>True if moved; otherwise false.</returns>
    public bool MovePath(string sourceRelativePath, string destinationRelativeDirectory)
    {
        sourceRelativePath = NormalizeRelativePath(sourceRelativePath);
        destinationRelativeDirectory = NormalizeRelativePath(destinationRelativeDirectory);

        try
        {
            string absDestDir = AbsAsset(destinationRelativeDirectory);
            if (!Directory.Exists(absDestDir)) return false;

            string absSource = AbsAsset(sourceRelativePath);
            string name = Path.GetFileName(absSource.TrimEnd(Path.DirectorySeparatorChar, '/', '\\'));
            if (string.IsNullOrWhiteSpace(name)) return false;

            string destRelativePath = NormalizeRelativePath(Path.Combine(destinationRelativeDirectory, name));

            if (IsSameRel(sourceRelativePath, destRelativePath)) return false;
            if (IsAncestorRel(sourceRelativePath, destRelativePath)) return false;

            return RenamePath(sourceRelativePath, destRelativePath);
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Meta rewrite

    private static void UpdateMetaSourcePathPrefix(string movedAbsFolder, string oldPrefix, string newPrefix)
    {
        string[] metaFiles;

        try
        {
            metaFiles = Directory.GetFiles(
                movedAbsFolder,
                "*" + AssetManager.C_ASSET_POSTFIX,
                SearchOption.AllDirectories);
        }
        catch
        {
            return;
        }

        oldPrefix = oldPrefix.TrimEnd('/', '\\');
        newPrefix = newPrefix.TrimEnd('/', '\\');

        for (int i = 0; i < metaFiles.Length; i++)
            UpdateMetaSourcePath(metaFiles[i], oldPrefix, newPrefix, isPrefix: true);
    }

    private static void UpdateMetaSourcePathExact(string metaAbsPath, string oldPath, string newPath)
        => UpdateMetaSourcePath(metaAbsPath, oldPath, newPath, isPrefix: false);

    private static void UpdateMetaSourcePath(string metaAbsPath, string oldValue, string newValue, bool isPrefix)
    {
        string text;

        try
        {
            text = File.ReadAllText(metaAbsPath);
        }
        catch
        {
            Log.Warn($"Could not read meta file: {metaAbsPath}");
            return;
        }

        string replaced = isPrefix
            ? ReplaceYamlSourcePathPrefix(text, oldValue, newValue)
            : ReplaceYamlSourcePathExact(text, oldValue, newValue);

        if (string.Equals(text, replaced, StringComparison.Ordinal))
            return;

        try
        {
            File.WriteAllText(metaAbsPath, replaced);
        }
        catch
        {
            Log.Warn($"Could not write meta file to: {metaAbsPath}");
        }
    }

    private static string ReplaceYamlSourcePathExact(string text, string oldValue, string newValue)
    {
        string needle = "sourcePath:";
        int idx = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return text;

        int lineStart = idx;
        int lineEnd = text.IndexOf('\n', idx);
        if (lineEnd < 0) lineEnd = text.Length;

        string line = text.Substring(lineStart, lineEnd - lineStart);
        if (!line.Contains(oldValue, StringComparison.OrdinalIgnoreCase)) return text;

        string replacedLine = line.Replace(oldValue, newValue, StringComparison.OrdinalIgnoreCase);
        return text.Substring(0, lineStart) + replacedLine + text.Substring(lineEnd);
    }

    private static string ReplaceYamlSourcePathPrefix(string text, string oldPrefix, string newPrefix)
    {
        string needle = "sourcePath:";
        int idx = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return text;

        int lineStart = idx;
        int lineEnd = text.IndexOf('\n', idx);
        if (lineEnd < 0) lineEnd = text.Length;

        string line = text.Substring(lineStart, lineEnd - lineStart);

        int q1 = line.IndexOf('"');
        int q2 = q1 >= 0 ? line.IndexOf('"', q1 + 1) : -1;

        if (q1 < 0 || q2 < 0) return text;

        string value = line.Substring(q1 + 1, q2 - q1 - 1);
        string normalizedValue = value.Replace('\\', '/');

        oldPrefix = oldPrefix.Replace('\\', '/').TrimEnd('/');
        newPrefix = newPrefix.Replace('\\', '/').TrimEnd('/');

        if (!normalizedValue.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
            return text;

        string replacedValue = newPrefix + normalizedValue.Substring(oldPrefix.Length);
        string replacedLine = line.Substring(0, q1 + 1) + replacedValue + line.Substring(q2);

        return text.Substring(0, lineStart) + replacedLine + text.Substring(lineEnd);
    }

    #endregion

    #region Paths

    private string AbsAsset(string relativePath)
        => Path.Combine(rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private string AbsMeta(string relativeFilePath)
        => AbsAsset(relativeFilePath) + AssetManager.C_ASSET_POSTFIX;

    private string AbsBin(string relativeFilePath)
        => Path.Combine(AbsBinDir(Path.GetDirectoryName(relativeFilePath) ?? ""), Path.GetFileName(relativeFilePath) + AssetManager.C_BINARY_ASSET_POSTFIX);

    private string AbsBinDir(string relativeDirectory)
    {
        string binRoot = binRootDirectory ?? "";
        if (string.IsNullOrWhiteSpace(binRoot)) return "";

        relativeDirectory = NormalizeRelativePath(relativeDirectory);
        return Path.Combine(binRoot, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
    }

    internal static string NormalizeRelativePath(string p)
{
    if (string.IsNullOrWhiteSpace(p)) return "";

    p = p.Replace('\\', '/').Trim();

    // Path.GetRelativePath may return "." for root. Normalize to empty.
    if (p == "." || p == "./") return "";

    while (p.StartsWith("./", StringComparison.Ordinal)) p = p.Substring(2);

    while (p.StartsWith("/", StringComparison.Ordinal)) p = p.Substring(1);
    while (p.EndsWith("/", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 1);

    return p;
}

internal static string CombineRelativePath(string a, string b)
    => NormalizeRelativePath(Path.Combine(NormalizeRelativePath(a), NormalizeRelativePath(b)));

    private static bool IsSameRel(string a, string b)
        => string.Equals(NormalizeRelativePath(a), NormalizeRelativePath(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsAncestorRel(string ancestor, string path)
    {
        ancestor = NormalizeRelativePath(ancestor);
        path = NormalizeRelativePath(path);

        if (ancestor.Length == 0) return false;
        if (IsSameRel(ancestor, path)) return true;

        string prefix = ancestor.TrimEnd('/') + "/";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Safe IO

    private static void SafeEnsureParentDirectory(string absPath)
    {
        string? parent = Path.GetDirectoryName(absPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);
    }

    private static void SafeDeleteFile(string absFile)
    {
        if (string.IsNullOrWhiteSpace(absFile)) return;

        try
        {
            if (File.Exists(absFile))
                File.Delete(absFile);
        }
        catch
        {
            Log.Warn($"Could not delete file: {absFile}");
        }
    }

    private static void SafeDeleteDirectoryRecursive(string absDir)
    {
        try
        {
            if (!Directory.Exists(absDir)) return;

            foreach (var f in Directory.GetFiles(absDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                }
                catch
                {
                    Log.Warn($"Could not delete file: {f}");
                }
            }

            foreach (var d in Directory.GetDirectories(absDir, "*", SearchOption.AllDirectories).OrderByDescending(s => s.Length))
            {
                try
                {
                    Directory.Delete(d, recursive: false);
                }
                catch
                {
                    Log.Warn($"Could not delete directory: {d}");
                }
            }
        }
        catch
        {
            Log.Warn($"Could not delete directory: {absDir}");
        }
    }

    private static void SafeDeleteDirectoryWithRetry(string absDir, int retries = 8, int sleepMs = 25)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                if (!Directory.Exists(absDir)) return;
                Directory.Delete(absDir, recursive: false);
                return;
            }
            catch
            {
                Thread.Sleep(sleepMs);
            }
        }
    }

    #endregion
}
