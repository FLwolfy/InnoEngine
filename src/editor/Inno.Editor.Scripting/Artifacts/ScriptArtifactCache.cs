using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Inno.Editor.Scripting;

internal sealed class ScriptArtifactCache
{
    private const int C_BUILD_KEY_LENGTH = 64;
    private const long C_DEFAULT_MAXIMUM_SIZE = 4L * 1024 * 1024 * 1024;
    private static readonly TimeSpan S_DEFAULT_GRACE_PERIOD = TimeSpan.FromDays(7);

    private readonly string m_root;

    internal ScriptArtifactCache(string root)
    {
        m_root = Path.GetFullPath(root);
        Directory.CreateDirectory(m_root);
    }

    internal int Collect(IEnumerable<string?> protectedDirectories)
    {
        var protectedPaths = protectedDirectories
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => Path.GetFullPath(value!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        DateTime cutoff = DateTime.UtcNow - S_DEFAULT_GRACE_PERIOD;
        Entry[] entries = Directory.EnumerateDirectories(m_root)
            .Where(static path =>
            {
                string name = Path.GetFileName(path);
                return name.Length == C_BUILD_KEY_LENGTH &&
                       name.All(static character => char.IsAsciiHexDigit(character));
            })
            .Select(CreateEntry)
            .OrderBy(static entry => entry.lastWriteUtc)
            .ToArray();
        long totalSize = entries.Sum(static entry => entry.size);
        int removed = 0;
        foreach (Entry entry in entries)
        {
            if (protectedPaths.Contains(entry.path))
                continue;
            if (entry.lastWriteUtc > cutoff && totalSize <= C_DEFAULT_MAXIMUM_SIZE)
                continue;
            try
            {
                Directory.Delete(entry.path, recursive: true);
                totalSize -= entry.size;
                removed++;
            }
            catch (IOException)
            {
                // A concurrent metadata reader can temporarily retain a platform handle.
            }
            catch (UnauthorizedAccessException)
            {
                // Read-only cache entries are retried during a later editor idle cycle.
            }
        }
        return removed;
    }

    private static Entry CreateEntry(string path)
    {
        long size = new DirectoryInfo(path)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(static file => file.Length);
        return new Entry(
            Path.GetFullPath(path),
            Directory.GetLastWriteTimeUtc(path),
            size);
    }

    private readonly record struct Entry(string path, DateTime lastWriteUtc, long size);
}
