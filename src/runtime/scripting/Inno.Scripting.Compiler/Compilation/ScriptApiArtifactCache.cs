using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Inno.Scripting.Compiler;

internal static class ScriptApiArtifactCache
{
    private const long C_MAXIMUM_SIZE = 512L * 1024 * 1024;
    private static readonly TimeSpan S_GRACE_PERIOD = TimeSpan.FromDays(7);

    internal static int Collect(string root, IEnumerable<string?> protectedDirectories)
    {
        if (!Directory.Exists(root))
            return 0;
        var protectedPaths = protectedDirectories
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Entry[] entries = Directory.EnumerateDirectories(root)
            .SelectMany(static profile => Directory.EnumerateDirectories(profile))
            .Select(TryCreateEntry)
            .OfType<Entry>()
            .OrderBy(static entry => entry.lastWriteUtc)
            .ToArray();
        long totalSize = entries.Sum(static entry => entry.size);
        DateTime cutoff = DateTime.UtcNow - S_GRACE_PERIOD;
        int removed = 0;
        foreach (Entry entry in entries)
        {
            if (protectedPaths.Contains(entry.path) ||
                entry.lastWriteUtc > cutoff && totalSize <= C_MAXIMUM_SIZE)
            {
                continue;
            }
            try
            {
                Directory.Delete(entry.path, recursive: true);
                totalSize -= entry.size;
                removed++;
            }
            catch (IOException)
            {
                // A compiler process can temporarily retain a reference artifact.
            }
            catch (UnauthorizedAccessException)
            {
                // Read-only entries are retried during a later collection pass.
            }
        }
        return removed;
    }

    private static Entry? TryCreateEntry(string path)
    {
        try
        {
            long size = new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(static file => file.Length);
            return new Entry(Path.GetFullPath(path), Directory.GetLastWriteTimeUtc(path), size);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record Entry(string path, DateTime lastWriteUtc, long size);
}
