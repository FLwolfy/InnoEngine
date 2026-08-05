using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Inno.Assets.File;

internal static class AssetChangeBatchNormalizer
{
    public static AssetChangedEvent[] Normalize(string rootPath, IReadOnlyList<AssetChangedEvent> rawBatch)
    {
        if (rawBatch.Count == 0)
            return [];

        var byPath = new Dictionary<string, Accumulator>(StringComparer.OrdinalIgnoreCase);
        var renameOldPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rawBatch.Count; i++)
        {
            AssetChangedEvent e = rawBatch[i];
            string path = NormalizeRelativePath(e.relativePath);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            if (!byPath.TryGetValue(path, out Accumulator acc))
                acc = new Accumulator(path);

            if (!string.IsNullOrWhiteSpace(e.oldRelativePath))
            {
                string oldPath = NormalizeRelativePath(e.oldRelativePath);
                if (!string.IsNullOrWhiteSpace(oldPath))
                {
                    acc.renameOldPath = oldPath;
                    renameOldPaths.Add(oldPath);
                }
            }

            WatcherChangeTypes type = e.changeType;
            if (type.HasFlag(WatcherChangeTypes.Renamed))
                acc.renamed = true;
            if (type.HasFlag(WatcherChangeTypes.Created))
                acc.created = true;
            if (type.HasFlag(WatcherChangeTypes.Changed))
                acc.changed = true;
            if (type.HasFlag(WatcherChangeTypes.Deleted))
                acc.deleted = true;

            byPath[path] = acc;
        }

        var normalized = new List<AssetChangedEvent>(byPath.Count);
        foreach (Accumulator acc in byPath.Values)
        {
            if (renameOldPaths.Contains(acc.path) && !acc.renamed)
                continue;

            if (TryBuildNormalized(rootPath, acc, out AssetChangedEvent normalizedEvent))
                normalized.Add(normalizedEvent);
        }

        return normalized
            .OrderByDescending(static e => e.changeType.HasFlag(WatcherChangeTypes.Renamed))
            .ThenByDescending(static e => e.changeType.HasFlag(WatcherChangeTypes.Deleted))
            .ThenBy(static e => e.relativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryBuildNormalized(string rootPath, in Accumulator acc, out AssetChangedEvent normalized)
    {
        if (acc.renamed && !string.IsNullOrWhiteSpace(acc.renameOldPath))
        {
            WatcherChangeTypes type = WatcherChangeTypes.Renamed;
            if (acc.changed)
                type |= WatcherChangeTypes.Changed;
            if (acc.created)
                type |= WatcherChangeTypes.Created;

            normalized = new AssetChangedEvent(acc.path, type, acc.renameOldPath);
            return true;
        }

        bool exists = ExistsAt(rootPath, acc.path);
        WatcherChangeTypes foldedType;

        if (acc.deleted && (acc.created || acc.changed))
            foldedType = exists ? WatcherChangeTypes.Changed : WatcherChangeTypes.Deleted;
        else if (acc.deleted)
            foldedType = WatcherChangeTypes.Deleted;
        else if (acc.created && acc.changed)
            foldedType = WatcherChangeTypes.Created | WatcherChangeTypes.Changed;
        else if (acc.created)
            foldedType = WatcherChangeTypes.Created;
        else if (acc.changed)
            foldedType = WatcherChangeTypes.Changed;
        else
            foldedType = 0;

        if (foldedType == 0)
        {
            normalized = default;
            return false;
        }

        normalized = new AssetChangedEvent(acc.path, foldedType);
        return true;
    }

    private static bool ExistsAt(string rootPath, string relativePath)
    {
        string full = Path.Combine(rootPath, relativePath);
        return System.IO.File.Exists(full) || Directory.Exists(full);
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

    private struct Accumulator(string path)
    {
        public string path = path;
        public bool created;
        public bool changed;
        public bool deleted;
        public bool renamed;
        public string renameOldPath = string.Empty;
    }
}
