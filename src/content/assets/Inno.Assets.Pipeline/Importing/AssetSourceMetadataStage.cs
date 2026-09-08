using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Core.IO;
using IOFile = System.IO.File;

namespace Inno.Assets.Pipeline;

internal sealed class AssetSourceMetadataStage
{
    private readonly Dictionary<string, Entry> m_entries = new(StringComparer.Ordinal);

    internal byte[]? Read(string path)
        => GetEntry(path).value?.ToArray();

    internal void Write(string path, byte[]? value)
        => GetEntry(path).value = value?.ToArray();

    internal void Commit(Action publishCatalog)
    {
        Entry[] snapshot = m_entries.Values.OrderBy(static entry => entry.path, StringComparer.Ordinal).ToArray();
        foreach (Entry entry in snapshot)
        {
            if (!Equal(entry.original, ReadPhysical(entry.path)))
                throw new IOException($"Asset source metadata '{entry.path}' changed while its candidate was being prepared.");
        }
        var applied = new List<Entry>();
        try
        {
            foreach (Entry entry in snapshot)
            {
                if (Equal(entry.original, entry.value))
                    continue;
                applied.Add(entry);
                WritePhysical(entry.path, entry.value);
            }
            publishCatalog();
            m_entries.Clear();
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            for (int index = applied.Count - 1; index >= 0; index--)
            {
                try { WritePhysical(applied[index].path, applied[index].original); }
                catch (Exception rollback) { failures.Add(rollback); }
            }
            if (failures.Count > 1)
                throw new AggregateException("Asset catalog publication and source metadata compensation failed.", failures);
            throw;
        }
    }

    private Entry GetEntry(string path)
    {
        if (!m_entries.TryGetValue(path, out Entry? entry))
        {
            byte[]? original = ReadPhysical(path);
            entry = new Entry(path, original);
            m_entries.Add(path, entry);
        }
        return entry;
    }

    private static bool Equal(byte[]? left, byte[]? right)
        => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    private static byte[]? ReadPhysical(string path)
        => IOFile.Exists(path) ? IOFile.ReadAllBytes(path) : null;

    private static void WritePhysical(string path, byte[]? value)
    {
        if (value is not null)
            AtomicFile.WriteAllBytes(path, value);
        else if (IOFile.Exists(path))
            IOFile.Delete(path);
    }

    private sealed class Entry(string path, byte[]? original)
    {
        internal string path { get; } = path;
        internal byte[]? original { get; } = original;
        internal byte[]? value { get; set; } = original;
    }
}
