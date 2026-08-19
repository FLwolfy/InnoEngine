using System;
using System.IO;

using Inno.Core.Serialization;

using IOFile = System.IO.File;

namespace Inno.Assets.Loader;

internal sealed class AssetCatalogStore
{
    private readonly string m_snapshotPath;
    private readonly string m_journalPath;
    private long m_revision;

    internal AssetCatalogStore(string libraryRoot)
    {
        string root = Path.Combine(libraryRoot, "AssetDatabase");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "Diagnostics"));
        m_snapshotPath = Path.Combine(root, "Catalog.snapshot");
        m_journalPath = Path.Combine(root, "Catalog.journal");
    }

    internal AssetMeta[] Load()
    {
        string path = IOFile.Exists(m_journalPath) ? m_journalPath : m_snapshotPath;
        if (!IOFile.Exists(path))
            return [];
        try
        {
            AssetCatalogSnapshot snapshot = SerializationManager.Deserialize<AssetCatalogSnapshot>(
                IOFile.ReadAllBytes(path));
            if (snapshot.schemaVersion != AssetCatalogSnapshot.C_SCHEMA_VERSION)
                return [];
            m_revision = snapshot.revision;
            if (string.Equals(path, m_journalPath, StringComparison.Ordinal))
                Commit(DeserializeEntries(snapshot.entries));
            return DeserializeEntries(snapshot.entries);
        }
        catch
        {
            return [];
        }
    }

    internal void Commit(AssetMeta[] entries)
    {
        var snapshot = new AssetCatalogSnapshot
        {
            revision = ++m_revision,
            entries = SerializeEntries(entries)
        };
        byte[] bytes = SerializationManager.Serialize(snapshot);
        AssetFileTransaction.WriteAtomic(m_journalPath, bytes);
        AssetFileTransaction.WriteAtomic(m_snapshotPath, bytes);
        if (IOFile.Exists(m_journalPath))
            IOFile.Delete(m_journalPath);
    }

    private static byte[][] SerializeEntries(AssetMeta[] entries)
    {
        var result = new byte[entries.Length][];
        for (int i = 0; i < entries.Length; i++)
            result[i] = SerializationManager.Serialize(entries[i]);
        return result;
    }

    private static AssetMeta[] DeserializeEntries(byte[][] entries)
    {
        var result = new AssetMeta[entries.Length];
        for (int i = 0; i < entries.Length; i++)
            result[i] = SerializationManager.Deserialize<AssetMeta>(entries[i]);
        return result;
    }
}
