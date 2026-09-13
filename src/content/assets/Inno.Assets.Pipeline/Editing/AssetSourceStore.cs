using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Inno.Core.IO;

namespace Inno.Assets.Pipeline;

/// <summary>Reads detached authoring bytes and saves them independently of successful import.</summary>
public sealed class AssetSourceStore
{
    private readonly AssetPipeline m_assets;
    private readonly AssetSerializationServices m_serialization;

    internal AssetSourceStore(AssetPipeline assets, AssetSerializationServices serialization)
    { m_assets = assets; m_serialization = serialization; }

    /// <summary>Reads a mounted source without modifying its canonical asset or compiled artifacts.</summary>
    /// <param name="path">Exact mounted source identity.</param>
    /// <returns>A detached source and optimistic concurrency fingerprint.</returns>
    public AssetSourceSnapshot Read(AssetPath path)
    {
        AssetSourceMount mount = Mount(path);
        byte[] bytes = File.ReadAllBytes(mount.Resolve(path.localPath));
        return new(bytes, Hash(bytes), mount.isReadOnly);
    }

    /// <summary>Encodes native asset properties with the owner's reference context and dependency capture.</summary>
    /// <typeparam name="TAsset">Native asset source type.</typeparam>
    /// <param name="asset">Detached editable value.</param>
    /// <returns>Native source bytes, with no canonical asset mutation.</returns>
    public byte[] Encode<TAsset>(TAsset asset) where TAsset : AssetObject
        => NativeAssetSourceSerialization.Export(asset, m_serialization);

    /// <summary>Restores a detached native value using current-generation asset references.</summary>
    /// <typeparam name="TAsset">Native asset source type.</typeparam>
    /// <param name="bytes">Native source bytes.</param>
    /// <returns>A detached value; referenced assets remain canonical read-only inputs.</returns>
    public TAsset Decode<TAsset>(ReadOnlySpan<byte> bytes) where TAsset : AssetObject
        => NativeAssetSourceSerialization.Import<TAsset>(bytes, m_serialization, out _);

    /// <summary>Atomically saves bytes after checking the source fingerprint; import is a separate operation.</summary>
    /// <param name="path">Writable mounted destination.</param>
    /// <param name="bytes">Complete serializable source, including invalid authoring states.</param>
    /// <param name="expectedHash">Last read/save fingerprint; null requires a nonexistent destination.</param>
    /// <returns>The saved fingerprint.</returns>
    public string Save(AssetPath path, byte[] bytes, string? expectedHash)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        AssetSourceMount mount = Mount(path);
        if (mount.isReadOnly) throw new InvalidOperationException("Installed assets are read-only. Copy the asset to the project before editing.");
        string destination = mount.Resolve(path.localPath);
        bool exists = File.Exists(destination);
        if (expectedHash is null ? exists : !exists || Hash(File.ReadAllBytes(destination)) != expectedHash)
            throw new IOException("The asset source changed externally. Draft and history are retained; reload or save a project copy explicitly.");
        if (expectedHash is null) AtomicFile.WriteAllBytes(destination, bytes, overwrite: false);
        else ReplaceExisting(destination, bytes, expectedHash);
        return Hash(bytes);
    }

    private static void ReplaceExisting(string destination, byte[] bytes, string expectedHash)
    {
        string token = Guid.NewGuid().ToString("N");
        string candidate = destination + ".staging-" + token;
        string replaced = destination + ".staging-previous-" + token;
        try
        {
            AtomicFile.WriteAllBytes(candidate, bytes, overwrite: false);
            File.Replace(candidate, destination, replaced);
            if (Hash(File.ReadAllBytes(replaced)) != expectedHash)
            {
                string conflict = destination + ".external-conflict-" + token;
                File.Move(replaced, conflict, overwrite: false);
                throw new IOException($"A concurrent external edit was preserved at '{conflict}'. Resolve both versions before saving again.");
            }
            File.Delete(replaced);
        }
        finally
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private AssetSourceMount Mount(AssetPath path)
    {
        if (!path.isValid) throw new ArgumentException("A valid mounted asset path is required.", nameof(path));
        return m_assets.sourceMounts.SingleOrDefault(mount => mount.id == path.source)
            ?? throw new InvalidOperationException($"Asset source mount '{path.source}' is unavailable.");
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

/// <summary>Contains detached authoring bytes and conflict-detection state.</summary>
public sealed class AssetSourceSnapshot
{
    private readonly byte[] m_bytes;
    internal AssetSourceSnapshot(byte[] bytes, string hash, bool readOnly)
    { m_bytes = bytes; contentHash = hash; isReadOnly = readOnly; }
    /// <summary>Gets a copy of the captured native source bytes.</summary>
    public byte[] bytes => (byte[])m_bytes.Clone();
    /// <summary>Gets the source fingerprint required by a subsequent save.</summary>
    public string contentHash { get; }
    /// <summary>Gets whether the installation source cannot be edited in place.</summary>
    public bool isReadOnly { get; }
}
