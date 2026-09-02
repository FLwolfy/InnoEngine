using System;
using System.IO;

using Inno.Core.Serialization;

using IOFile = System.IO.File;

namespace Inno.Assets.Pipeline;

internal sealed class AssetCatalogStore
{
    private readonly SerializationRegistry? m_serialization;
    private readonly SerializationGeneration? m_serializationGeneration;

    private string m_databaseRoot;
    private string m_snapshotPath;
    private string m_journalPath;
    private long m_revision;

    internal AssetCatalogStore(string libraryRoot, SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        m_serialization = serialization;
        m_databaseRoot = string.Empty;
        m_snapshotPath = string.Empty;
        m_journalPath = string.Empty;
        Bind(libraryRoot);
    }

    internal AssetCatalogStore(string libraryRoot, SerializationGeneration serialization)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        m_serializationGeneration = serialization;
        m_databaseRoot = string.Empty;
        m_snapshotPath = string.Empty;
        m_journalPath = string.Empty;
        Bind(libraryRoot);
    }

    internal AssetMeta[] Load()
    {
        string path = IOFile.Exists(m_journalPath) ? m_journalPath : m_snapshotPath;
        if (!IOFile.Exists(path))
            return [];
        try
        {
            AssetCatalogSnapshot snapshot = Deserialize<AssetCatalogSnapshot>(IOFile.ReadAllBytes(path));
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
        byte[] bytes = Serialize(snapshot);
        AssetFileTransaction.WriteAtomic(m_journalPath, bytes);
        AssetFileTransaction.WriteAtomic(m_snapshotPath, bytes);
        if (IOFile.Exists(m_journalPath))
            IOFile.Delete(m_journalPath);
    }

    internal void CopyLatestTo(string destinationLibraryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationLibraryRoot);
        string? sourcePath = LatestPathOrNull();
        if (sourcePath is null)
            return;
        string destinationRoot = GetDatabaseRoot(destinationLibraryRoot);
        Directory.CreateDirectory(destinationRoot);
        AssetFileTransaction.WriteAtomic(
            Path.Combine(destinationRoot, "Catalog.snapshot"),
            IOFile.ReadAllBytes(sourcePath));
    }

    internal void PromoteTo(string destinationLibraryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationLibraryRoot);
        string destinationRoot = GetDatabaseRoot(destinationLibraryRoot);
        string destinationSnapshot = Path.Combine(destinationRoot, "Catalog.snapshot");
        string destinationJournal = Path.Combine(destinationRoot, "Catalog.journal");
        string? sourcePath = LatestPathOrNull();
        Directory.CreateDirectory(destinationRoot);
        if (sourcePath is null)
        {
            if (IOFile.Exists(destinationSnapshot))
                IOFile.Delete(destinationSnapshot);
            if (IOFile.Exists(destinationJournal))
                IOFile.Delete(destinationJournal);
        }
        else
        {
            byte[] bytes = IOFile.ReadAllBytes(sourcePath);
            AssetFileTransaction.WriteAtomic(destinationSnapshot, bytes);
            if (IOFile.Exists(destinationJournal))
                IOFile.Delete(destinationJournal);
        }

        m_databaseRoot = destinationRoot;
        m_snapshotPath = destinationSnapshot;
        m_journalPath = destinationJournal;
    }

    private void Bind(string libraryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        m_databaseRoot = GetDatabaseRoot(libraryRoot);
        Directory.CreateDirectory(m_databaseRoot);
        Directory.CreateDirectory(Path.Combine(m_databaseRoot, "Diagnostics"));
        m_snapshotPath = Path.Combine(m_databaseRoot, "Catalog.snapshot");
        m_journalPath = Path.Combine(m_databaseRoot, "Catalog.journal");
    }

    private string? LatestPathOrNull()
    {
        if (IOFile.Exists(m_journalPath))
            return m_journalPath;
        return IOFile.Exists(m_snapshotPath) ? m_snapshotPath : null;
    }

    private static string GetDatabaseRoot(string libraryRoot)
        => Path.Combine(Path.GetFullPath(libraryRoot), "AssetDatabase");

    private byte[][] SerializeEntries(AssetMeta[] entries)
    {
        var result = new byte[entries.Length][];
        for (int i = 0; i < entries.Length; i++)
            result[i] = Serialize(entries[i]);
        return result;
    }

    private AssetMeta[] DeserializeEntries(byte[][] entries)
    {
        var result = new AssetMeta[entries.Length];
        for (int i = 0; i < entries.Length; i++)
            result[i] = Deserialize<AssetMeta>(entries[i]);
        return result;
    }

    private byte[] Serialize<T>(T value)
        where T : class, ISerializable
        => m_serializationGeneration is null
            ? m_serialization!.Serialize(value)
            : m_serializationGeneration.Serialize(value);

    private T Deserialize<T>(ReadOnlySpan<byte> bytes)
        where T : class, ISerializable
        => m_serializationGeneration is null
            ? m_serialization!.Deserialize<T>(bytes)
            : m_serializationGeneration.Deserialize<T>(bytes);
}
