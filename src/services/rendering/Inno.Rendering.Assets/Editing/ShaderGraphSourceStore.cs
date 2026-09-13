using System;
using System.IO;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Core.Serialization;

namespace Inno.Rendering.Assets;

/// <summary>Reads and atomically saves the sole shader graph source format without requiring successful compilation.</summary>
public sealed class ShaderGraphSourceStore
{
    private readonly AssetSourceStore m_sources;
    private readonly SerializationRegistry m_serialization;

    /// <summary>Uses the authoritative asset mounts and native graph serializer.</summary>
    /// <param name="assets">Owner-thread authoring asset pipeline.</param>
    /// <param name="serialization">Current native converter registry.</param>
    public ShaderGraphSourceStore(AssetPipeline assets, SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(assets);
        m_sources = assets.CreateSourceStore();
        m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
    }

    /// <summary>Reads source directly, so a broken graph remains editable even when import has no successful artifact.</summary>
    /// <param name="path">Exact graph path within an active asset mount.</param>
    /// <returns>A detached native source snapshot with its conflict-detection fingerprint.</returns>
    public ShaderGraphSourceSnapshot Read(AssetPath path)
    {
        Validate(path);
        AssetSourceSnapshot source = m_sources.Read(path);
        return new(GraphDocumentCodec.Decode(source.bytes, m_serialization), source.contentHash, source.isReadOnly);
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
        Validate(path);
        return m_sources.Save(path, GraphDocumentCodec.Encode(graph, m_serialization), expectedHash);
    }

    private static void Validate(AssetPath path)
    {
        if (!path.isValid || !path.localPath.EndsWith(".ishader", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A shader graph source must use the .ishader extension.", nameof(path));
    }

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
