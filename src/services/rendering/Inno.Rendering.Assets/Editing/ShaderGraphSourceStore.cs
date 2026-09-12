using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Core.IO;
using Inno.Core.Serialization;

namespace Inno.Rendering.Assets;

/// <summary>Reads and atomically saves the sole shader graph source format without requiring successful compilation.</summary>
public sealed class ShaderGraphSourceStore
{
    private readonly AssetPipeline m_assets;
    private readonly SerializationRegistry m_serialization;

    /// <summary>Uses the authoritative asset mounts and native graph serializer.</summary>
    /// <param name="assets">Owner-thread authoring asset pipeline.</param>
    /// <param name="serialization">Current native converter registry.</param>
    public ShaderGraphSourceStore(AssetPipeline assets, SerializationRegistry serialization)
    {
        m_assets = assets ?? throw new ArgumentNullException(nameof(assets));
        m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
    }

    /// <summary>Reads source directly, so a broken graph remains editable even when import has no successful artifact.</summary>
    /// <param name="path">Exact graph path within an active asset mount.</param>
    /// <returns>A detached native source snapshot with its conflict-detection fingerprint.</returns>
    public ShaderGraphSourceSnapshot Read(AssetPath path)
    {
        AssetSourceMount mount = Mount(path);
        byte[] bytes = File.ReadAllBytes(mount.Resolve(path.localPath));
        return new(GraphDocumentCodec.Decode(bytes, m_serialization), Hash(bytes), mount.isReadOnly);
    }

    /// <summary>Saves serializable graph records independently of import or native compilation success.</summary>
    /// <param name="path">Exact destination within a writable active mount.</param>
    /// <param name="graph">Complete graph, including missing nodes and unresolved ports.</param>
    /// <param name="expectedHash">Fingerprint from the last read/save; null requires a new, nonexistent file.</param>
    /// <returns>The saved fingerprint. Import runs separately through the normal asset watcher.</returns>
    /// <exception cref="IOException">The source changed externally, disappeared, or could not be written.</exception>
    /// <exception cref="InvalidOperationException">The mount is unavailable or read-only.</exception>
    public string Save(AssetPath path, GraphDocument graph, string? expectedHash)
    {
        ArgumentNullException.ThrowIfNull(graph);
        AssetSourceMount mount = Mount(path);
        if (mount.isReadOnly) throw new InvalidOperationException("Installed shader assets are read-only. Copy the shader to the project before editing.");
        string destination = mount.Resolve(path.localPath);
        byte[] bytes = GraphDocumentCodec.Encode(graph, m_serialization);
        bool exists = File.Exists(destination);
        if (expectedHash is null ? exists : !exists || Hash(File.ReadAllBytes(destination)) != expectedHash)
            throw new IOException("The shader source changed externally. Unsaved graph and history are retained; reload or save a project copy explicitly.");
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
            // Capture the exact file replaced by the atomic operation, not only the earlier optimistic read.
            // An uncooperative external writer can race a portable file API; preserve that version and report it explicitly.
            File.Replace(candidate, destination, replaced);
            if (Hash(File.ReadAllBytes(replaced)) != expectedHash)
            {
                string conflict = destination + ".external-conflict-" + token;
                File.Move(replaced, conflict, overwrite: false);
                throw new IOException($"A concurrent external edit was preserved at '{conflict}'. The graph and recovery are retained; resolve both versions before saving again.");
            }
            File.Delete(replaced);
        }
        finally
        {
            if (File.Exists(candidate)) File.Delete(candidate);
            // Never delete the replaced version after a failed verification or recovery move.
        }
    }

    private AssetSourceMount Mount(AssetPath path)
    {
        if (!path.isValid || !path.localPath.EndsWith(".ishader", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A shader graph source must use the .ishader extension.", nameof(path));
        return m_assets.sourceMounts.SingleOrDefault(mount => mount.id == path.source)
            ?? throw new InvalidOperationException($"Shader source mount '{path.source}' is unavailable.");
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

/// <summary>Contains detached graph source state, not a compiled or live runtime asset.</summary>
public sealed class ShaderGraphSourceSnapshot
{
    private readonly GraphDocument m_document;
    internal ShaderGraphSourceSnapshot(GraphDocument document, string hash, bool readOnly)
    { m_document = document; contentHash = hash; isReadOnly = readOnly; }
    /// <summary>Gets a detached copy of authored graph records.</summary>
    public GraphDocument document => m_document.Clone();
    /// <summary>Gets the source fingerprint used to reject external-edit conflicts.</summary>
    public string contentHash { get; }
    /// <summary>Gets whether the source belongs to an immutable installation mount.</summary>
    public bool isReadOnly { get; }
}
