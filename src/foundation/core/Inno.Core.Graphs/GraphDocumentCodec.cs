using System;
using System.Linq;
using Inno.Core.Serialization;

namespace Inno.Core.Graphs;

/// <summary>
/// Persists neutral graph documents through the common Inno serialization pipeline.
/// </summary>
public static class GraphDocumentCodec
{
    /// <summary>
    /// Encodes a graph document into deterministic native bytes.
    /// </summary>
    /// <param name="document">
    /// Document to encode.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the converter generation for this operation.
    /// </param>
    /// <returns>
    /// Native graph bytes containing stable IDs and neutral values only.
    /// </returns>
    public static byte[] Encode(
        GraphDocument document,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(serialization);
        return serialization.Encode(writer =>
        {
            writer.WriteObjectArray("nodes", document.nodes, static (nodeWriter, node) =>
            {
                nodeWriter.Write("id", node.id.value);
                nodeWriter.Write("definition", node.definitionId);
                nodeWriter.Write("positionX", node.position.x);
                nodeWriter.Write("positionY", node.position.y);
                nodeWriter.WriteObjectArray(
                    "values",
                    node.values.OrderBy(static pair => pair.Key, StringComparer.Ordinal),
                    static (valueWriter, pair) =>
                    {
                        valueWriter.Write("id", pair.Key);
                        valueWriter.Write("data", pair.Value.ToArray());
                    });
            });
            writer.WriteObjectArray("edges", document.edges, static (edgeWriter, edge) =>
            {
                edgeWriter.Write("id", edge.id.value);
                edgeWriter.Write("outputNode", edge.output.nodeId.value);
                edgeWriter.Write("outputPort", edge.output.portId.value);
                edgeWriter.Write("inputNode", edge.input.nodeId.value);
                edgeWriter.Write("inputPort", edge.input.portId.value);
            });
            writer.WriteObjectArray(
                "metadata",
                document.metadata.OrderBy(static pair => pair.Key, StringComparer.Ordinal),
                static (metadataWriter, pair) =>
                {
                    metadataWriter.Write("id", pair.Key);
                    metadataWriter.Write("data", pair.Value.ToArray());
                });
        });
    }

    /// <summary>
    /// Decodes one current native graph document.
    /// </summary>
    /// <param name="bytes">
    /// Complete native graph payload.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns the converter generation for this operation.
    /// </param>
    /// <returns>
    /// A detached mutable neutral graph document.
    /// </returns>
    public static GraphDocument Decode(
        ReadOnlySpan<byte> bytes,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        return serialization.Decode(bytes, reader =>
        {
            var document = new GraphDocument();
            foreach (SerializationReader nodeReader in reader.ReadObjectArray("nodes"))
            {
                var node = new GraphNodeRecord(
                    new GraphNodeId(nodeReader.Read<string>("id")),
                    nodeReader.Read<string>("definition"))
                {
                    position = new GraphPosition(
                        nodeReader.Read<float>("positionX"),
                        nodeReader.Read<float>("positionY"))
                };
                foreach (SerializationReader valueReader in nodeReader.ReadObjectArray("values"))
                {
                    node.SetValue(
                        valueReader.Read<string>("id"),
                        new GraphSerializedValue(valueReader.Read<byte[]>("data")));
                }
                document.AddNode(node);
            }
            foreach (SerializationReader edgeReader in reader.ReadObjectArray("edges"))
            {
                document.AddEdge(new GraphEdgeRecord(
                    new GraphEdgeId(edgeReader.Read<string>("id")),
                    new GraphEndpoint(
                        new GraphNodeId(edgeReader.Read<string>("outputNode")),
                        new GraphPortId(edgeReader.Read<string>("outputPort"))),
                    new GraphEndpoint(
                        new GraphNodeId(edgeReader.Read<string>("inputNode")),
                        new GraphPortId(edgeReader.Read<string>("inputPort")))));
            }
            foreach (SerializationReader metadataReader in reader.ReadObjectArray("metadata"))
            {
                document.SetMetadata(
                    metadataReader.Read<string>("id"),
                    new GraphSerializedValue(metadataReader.Read<byte[]>("data")));
            }
            return document;
        });
    }
}
