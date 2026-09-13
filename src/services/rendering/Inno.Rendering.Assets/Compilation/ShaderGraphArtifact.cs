using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Rendering.Shaders;
using Inno.Extensibility.Types;

namespace Inno.Rendering.Assets;

/// <summary>Persists graph and frozen function inputs as an authoring artifact; Player export removes this payload.</summary>
public static class ShaderGraphArtifact
{
    /// <summary>Identifies the authoring-only graph and frozen source output.</summary>
    public const string outputName = "shader-graph";

    /// <summary>Freezes an authored graph and its target-expanded function dependencies without publishing an asset.</summary>
    /// <param name="graph">Detached authoring document, including incomplete source records.</param>
    /// <param name="types">Current leased authoring generation.</param>
    /// <param name="serialization">Owner converters.</param>
    /// <param name="context">Complete owner reference and dependency context.</param>
    /// <param name="readSource">Reads an immutable function bundle by stable identity and diagnostic last-known path.</param>
    /// <param name="cancellationToken">Cancellation before target expansion and dependency reads.</param>
    /// <returns>Native immutable authoring artifact bytes usable by import or isolated preview compilation.</returns>
    public static byte[] Capture(GraphDocument graph, TypeCatalog types, SerializationRegistry serialization,
        SerializationContext context, Func<Guid, string, byte[]> readSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readSource);
        using IDisposable operation = types.AcquireOperation("Capture shader authoring candidate");
        using var targets = new ShaderTargetRegistry(types);
        GraphDocument program = targets.Expand(graph, serialization, context, cancellationToken);
        var sources = new Dictionary<GraphNodeId, byte[]>();
        foreach (GraphNodeRecord node in program.nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.definitionId != "inno.shader.source") continue;
            Guid id = ShaderGraphDocument.Read(node, "sourceId", Guid.Empty, serialization, context);
            if (id == Guid.Empty) continue;
            string path = ShaderGraphDocument.Read(node, "sourcePath", "", serialization, context);
            sources.Add(node.id, readSource(id, path));
        }
        return Encode(graph, sources, serialization, program);
    }

    /// <summary>Reads the target-expanded runtime interface from the same frozen candidate as its computations.</summary>
    /// <param name="bytes">Immutable captured authoring artifact.</param>
    /// <param name="serialization">Owner converters.</param>
    /// <param name="context">Complete owner asset references.</param>
    /// <returns>Detached definition paired with these exact graph programs.</returns>
    public static ShaderDefinition ReadDefinition(ReadOnlySpan<byte> bytes, SerializationRegistry serialization, SerializationContext context)
        => ShaderGraphDocument.ReadDefinition(GraphDocumentCodec.Decode(serialization.Deserialize<ArtifactData>(bytes).program, serialization), serialization, context);

    /// <summary>Reads a retained immutable authoring snapshot through its explicit artifact owner.</summary>
    /// <param name="shader">Stable Shader asset identity.</param>
    /// <param name="artifacts">Current authoring artifact lookup.</param>
    /// <returns>Detached graph bytes; absent authoring data is an error, never a runtime fallback.</returns>
    public static byte[] Read(ShaderAsset shader, Inno.Assets.IAssetArtifactLookup artifacts)
    {
        ArgumentNullException.ThrowIfNull(shader);
        ArgumentNullException.ThrowIfNull(artifacts);
        using Inno.Assets.ArtifactLease lease = artifacts.AcquireArtifact(shader.identity.persistentId, outputName);
        return System.IO.File.ReadAllBytes(lease.info.absolutePath);
    }

    /// <summary>Fingerprints semantic graph records and every frozen source dependency, excluding node positions and reserved Editor metadata.</summary>
    /// <param name="bytes">Committed graph authoring artifact.</param>
    /// <param name="serialization">Owner native converter registry.</param>
    /// <returns>A stable semantic identity; target, variant and extension generation remain separate compiler cache inputs.</returns>
    public static string GetSemanticHash(ReadOnlySpan<byte> bytes, SerializationRegistry serialization)
    {
        ArtifactData data = serialization.Deserialize<ArtifactData>(bytes);
        GraphDocument graph = GraphDocumentCodec.Decode(data.program, serialization);
        foreach (GraphNodeRecord node in graph.nodes)
        {
            node.position = default;
            foreach (string key in node.values.Keys.Where(static key => key.StartsWith("inno.editor.", StringComparison.Ordinal)).ToArray())
                node.RemoveValue(key);
        }
        foreach (string key in graph.metadata.Keys.Where(static key => key.StartsWith("inno.editor.", StringComparison.Ordinal)).ToArray())
            graph.RemoveMetadata(key);
        data.document = [];
        data.program = GraphDocumentCodec.Encode(graph, serialization);
        return Convert.ToHexString(SHA256.HashData(serialization.Serialize(data)));
    }

    /// <summary>Captures one native immutable graph import candidate.</summary>
    /// <param name="graph">Authored graph with original node and port identities.</param>
    /// <param name="sources">Frozen function bundles keyed by source node identity.</param>
    /// <param name="serialization">Owner native converter registry.</param>
    /// <param name="program">Target-expanded explicit stages, or null when the authored graph already contains them.</param>
    /// <returns>Deterministic authoring artifact bytes without live objects.</returns>
    public static byte[] Encode(GraphDocument graph, IReadOnlyDictionary<GraphNodeId, byte[]> sources, SerializationRegistry serialization,
        GraphDocument? program = null)
        => serialization.Serialize(new ArtifactData
        {
            document = GraphDocumentCodec.Encode(graph, serialization),
            program = GraphDocumentCodec.Encode(program ?? graph, serialization),
            sources = sources.OrderBy(static pair => pair.Key.value, StringComparer.Ordinal)
                .Select(static pair => new SourceData { node = pair.Key.value, bundle = pair.Value.ToArray() }).ToArray()
        });

    /// <summary>Restores the imported graph for authoring or source export.</summary>
    /// <param name="bytes">Current authoring artifact bytes.</param>
    /// <param name="serialization">Owner native converter registry.</param>
    /// <returns>A detached graph retaining all authored records.</returns>
    public static GraphDocument ReadDocument(ReadOnlySpan<byte> bytes, SerializationRegistry serialization)
        => GraphDocumentCodec.Decode(serialization.Deserialize<ArtifactData>(bytes).document, serialization);

    /// <summary>Analyzes frozen functions and lowers the graph in the owner's active generation.</summary>
    /// <param name="bytes">Committed authoring artifact bytes.</param>
    /// <param name="implementationId">Selected adapter implementation identity.</param>
    /// <param name="nodes">Shared node compiler registry.</param>
    /// <param name="frontends">Shared language frontend registry.</param>
    /// <param name="serialization">Owner native converter registry.</param>
    /// <param name="context">Complete owner asset/reference context.</param>
    /// <param name="defines">Exact target/variant preprocessing inputs.</param>
    /// <returns>Complete typed stages or graph diagnostics, never a partial candidate.</returns>
    public static ShaderGraphProgramResult Lower(ReadOnlySpan<byte> bytes, string implementationId,
        ShaderNodeCompilerRegistry nodes, ShaderSourceFrontendRegistry frontends, SerializationRegistry serialization,
        SerializationContext context, IReadOnlyDictionary<string, string>? defines = null)
    {
        ArtifactData data = serialization.Deserialize<ArtifactData>(bytes);
        GraphDocument graph = GraphDocumentCodec.Decode(data.program, serialization);
        var sources = data.sources.ToDictionary(static source => new GraphNodeId(source.node),
            source => frontends.AnalyzeModule(ShaderSourceBundle.Decode(source.bundle, serialization, defines)));
        return new ShaderGraphProgramCompiler(nodes).Lower(graph, implementationId, sources, serialization, context);
    }

    private sealed class ArtifactData : ISerializable
    {
        /// <summary>Gets or sets native graph bytes.</summary>
        [SerializableProperty] public byte[] document { get; set; } = [];
        /// <summary>Gets or sets explicit target-expanded stage graph bytes.</summary>
        [SerializableProperty] public byte[] program { get; set; } = [];
        /// <summary>Gets or sets frozen functions by node identity.</summary>
        [SerializableProperty] public SourceData[] sources { get; set; } = [];
    }
    private struct SourceData
    {
        /// <summary>Gets or sets the stable source node identity.</summary>
        public string node { get; set; }
        /// <summary>Gets or sets native frozen function bytes.</summary>
        public byte[] bundle { get; set; }
    }
}
