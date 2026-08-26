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
        string assemblyRoot = Path.Combine(m_root, ".assemblies");
        IEnumerable<string> candidates = Directory.EnumerateDirectories(m_root);
        if (Directory.Exists(assemblyRoot))
            candidates = candidates.Concat(Directory.EnumerateDirectories(assemblyRoot));
        var discoveredEntries = new List<Entry>();
        foreach (string path in candidates.Where(IsArtifactDirectory))
        {
            if (TryCreateEntry(path, out Entry entry))
                discoveredEntries.Add(entry);
        }
        Entry[] entries = discoveredEntries
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
        removed += CollectStaleStaging(Path.Combine(m_root, ".staging"), cutoff);
        removed += CollectStaleStaging(Path.Combine(m_root, ".assembly-staging"), cutoff);
        return removed;
    }

    private static int CollectStaleStaging(string root, DateTime cutoff)
    {
        if (!Directory.Exists(root))
            return 0;

        int removed = 0;
        foreach (string path in Directory.EnumerateDirectories(root))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(path) > cutoff)
                    continue;
                Directory.Delete(path, recursive: true);
                removed++;
            }
            catch (IOException)
            {
                // A live or recently interrupted compiler can temporarily retain a platform handle.
            }
            catch (UnauthorizedAccessException)
            {
                // Read-only staging directories are retried during a later editor idle cycle.
            }
        }
        return removed;
    }

    private static bool IsArtifactDirectory(string path)
    {
        string name = Path.GetFileName(path);
        return name.Length == C_BUILD_KEY_LENGTH &&
               name.All(static character => char.IsAsciiHexDigit(character));
    }

    private static bool TryCreateEntry(string path, out Entry entry)
    {
        try
        {
            long size = new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(static file => file.Length);
            entry = new Entry(
                Path.GetFullPath(path),
                Directory.GetLastWriteTimeUtc(path),
                size);
            return true;
        }
        catch (IOException)
        {
            entry = default;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            entry = default;
            return false;
        }
    }

    private readonly record struct Entry(string path, DateTime lastWriteUtc, long size);
}
