using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Inno.Core.Graphs;
using Inno.Editor.Interactions;

namespace Inno.Editor.Graph;

internal static class GraphHistoryDocumentCodec
{
    private static readonly HashSet<string> S_ROOT = new(["nodes", "edges", "metadata"], StringComparer.Ordinal);
    private static readonly HashSet<string> S_NODE = new(["id", "definition", "position", "values"], StringComparer.Ordinal);
    private static readonly HashSet<string> S_EDGE = new(["id", "output", "input"], StringComparer.Ordinal);
    private static readonly HashSet<string> S_ENDPOINT = new(["node", "port"], StringComparer.Ordinal);

    public static byte[] Encode(GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("nodes");
            writer.WriteStartArray();
            foreach (GraphNodeRecord node in document.nodes)
            {
                writer.WriteStartObject();
                writer.WriteString("id", node.id.value);
                writer.WriteString("definition", node.definitionId);
                writer.WritePropertyName("position");
                writer.WriteStartArray();
                writer.WriteNumberValue(node.position.x);
                writer.WriteNumberValue(node.position.y);
                writer.WriteEndArray();
                writer.WritePropertyName("values");
                writer.WriteStartObject();
                foreach ((string id, GraphSerializedValue value) in node.values)
                {
                    writer.WritePropertyName(id);
                    using JsonDocument property = JsonDocument.Parse(value.json);
                    property.RootElement.WriteTo(writer);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("edges");
            writer.WriteStartArray();
            foreach (GraphEdgeRecord edge in document.edges)
            {
                writer.WriteStartObject();
                writer.WriteString("id", edge.id.value);
                WriteEndpoint(writer, "output", edge.output);
                WriteEndpoint(writer, "input", edge.input);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("metadata");
            writer.WriteStartObject();
            foreach ((string key, GraphSerializedValue value) in document.metadata)
            {
                writer.WritePropertyName(key);
                using JsonDocument metadata = JsonDocument.Parse(value.json);
                metadata.RootElement.WriteTo(writer);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static GraphDocument Decode(ReadOnlySpan<byte> bytes)
    {
        using JsonDocument source = JsonDocument.Parse(bytes.ToArray());
        JsonElement root = RequireObject(source.RootElement, "$", S_ROOT);
        var document = new GraphDocument();
        foreach (JsonElement nodeElement in root.GetProperty("nodes").EnumerateArray())
        {
            JsonElement nodeObject = RequireObject(nodeElement, "$.nodes[]", S_NODE);
            var node = new GraphNodeRecord(
                new GraphNodeId(RequireString(nodeObject, "id")),
                RequireString(nodeObject, "definition"));
            JsonElement position = nodeObject.GetProperty("position");
            if (position.ValueKind != JsonValueKind.Array || position.GetArrayLength() != 2)
            {
                throw new InvalidDataException("Graph node position must contain exactly two numbers.");
            }

            node.position = new GraphPosition(position[0].GetSingle(), position[1].GetSingle());
            JsonElement values = nodeObject.GetProperty("values");
            if (values.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Graph node values must be an object.");
            }

            foreach (JsonProperty property in values.EnumerateObject())
            {
                node.SetValue(property.Name, new GraphSerializedValue(property.Value.GetRawText()));
            }

            document.AddNode(node);
        }

        foreach (JsonElement edgeElement in root.GetProperty("edges").EnumerateArray())
        {
            JsonElement edge = RequireObject(edgeElement, "$.edges[]", S_EDGE);
            document.AddEdge(new GraphEdgeRecord(
                new GraphEdgeId(RequireString(edge, "id")),
                ReadEndpoint(edge.GetProperty("output")),
                ReadEndpoint(edge.GetProperty("input"))));
        }

        JsonElement metadata = root.GetProperty("metadata");
        if (metadata.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Graph metadata must be an object.");
        }

        foreach (JsonProperty property in metadata.EnumerateObject())
        {
            document.SetMetadata(property.Name, new GraphSerializedValue(property.Value.GetRawText()));
        }

        return document;
    }

    private static void WriteEndpoint(Utf8JsonWriter writer, string name, GraphEndpoint endpoint)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteString("node", endpoint.nodeId.value);
        writer.WriteString("port", endpoint.portId.value);
        writer.WriteEndObject();
    }

    private static GraphEndpoint ReadEndpoint(JsonElement element)
    {
        JsonElement endpoint = RequireObject(element, "$.edges[].endpoint", S_ENDPOINT);
        return new GraphEndpoint(
            new GraphNodeId(RequireString(endpoint, "node")),
            new GraphPortId(RequireString(endpoint, "port")));
    }

    private static JsonElement RequireObject(
        JsonElement element,
        string path,
        IReadOnlySet<string> allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{path} must be an object.");
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new InvalidDataException($"{path}.{property.Name} is not a current graph field.");
            }
        }

        foreach (string required in allowed)
        {
            if (!element.TryGetProperty(required, out _))
            {
                throw new InvalidDataException($"{path}.{required} is required.");
            }
        }

        return element;
    }

    private static string RequireString(JsonElement element, string property)
        => element.GetProperty(property).GetString()
            ?? throw new InvalidDataException($"Graph property '{property}' must be a string.");
}

internal sealed record GraphHistoryData(
    string documentId,
    byte[] before,
    byte[] after,
    long timestamp)
{
    public const string C_KIND = "editor.graph.document";
    private static readonly UTF8Encoding S_UTF8 = new(false, true);

    public static EditorHistoryChange CreateChange(
        string documentId,
        ReadOnlySpan<byte> before,
        ReadOnlySpan<byte> after,
        string? mergeKey)
    {
        var data = new GraphHistoryData(documentId, before.ToArray(), after.ToArray(), Stopwatch.GetTimestamp());
        return new EditorHistoryChange(C_KIND, EditorHistoryPayload.FromBytes(data.Encode()), mergeKey);
    }

    public byte[] Encode()
    {
        byte[] id = S_UTF8.GetBytes(documentId);
        byte[] result = new byte[checked((sizeof(int) * 3) + sizeof(long) + id.Length + before.Length + after.Length)];
        Span<byte> span = result;
        BinaryPrimitives.WriteInt32LittleEndian(span, id.Length);
        id.CopyTo(span[sizeof(int)..]);
        int offset = sizeof(int) + id.Length;
        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], before.Length);
        offset += sizeof(int);
        before.CopyTo(span[offset..]);
        offset += before.Length;
        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], after.Length);
        offset += sizeof(int);
        after.CopyTo(span[offset..]);
        offset += after.Length;
        BinaryPrimitives.WriteInt64LittleEndian(span[offset..], timestamp);
        return result;
    }

    public static GraphHistoryData Decode(ReadOnlySpan<byte> payload)
    {
        int offset = 0;
        int idLength = ReadLength(payload, ref offset);
        string id = S_UTF8.GetString(ReadSlice(payload, ref offset, idLength));
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        int beforeLength = ReadLength(payload, ref offset);
        byte[] before = ReadSlice(payload, ref offset, beforeLength).ToArray();
        int afterLength = ReadLength(payload, ref offset);
        byte[] after = ReadSlice(payload, ref offset, afterLength).ToArray();
        if (payload.Length - offset != sizeof(long))
        {
            throw new InvalidDataException("Graph History payload has an invalid timestamp boundary.");
        }

        long timestamp = BinaryPrimitives.ReadInt64LittleEndian(payload[offset..]);
        _ = GraphHistoryDocumentCodec.Decode(before);
        _ = GraphHistoryDocumentCodec.Decode(after);
        return new GraphHistoryData(id, before, after, timestamp);
    }

    private static int ReadLength(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (payload.Length - offset < sizeof(int))
        {
            throw new InvalidDataException("Graph History payload is truncated.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
        offset += sizeof(int);
        if (length < 0 || length > payload.Length - offset)
        {
            throw new InvalidDataException("Graph History payload contains an invalid length.");
        }

        return length;
    }

    private static ReadOnlySpan<byte> ReadSlice(ReadOnlySpan<byte> payload, ref int offset, int length)
    {
        ReadOnlySpan<byte> result = payload.Slice(offset, length);
        offset += length;
        return result;
    }
}

internal readonly record struct GraphHistoryTransitionResult(
    EditorHistoryResult result,
    bool stateIntegrityLost);

internal static class GraphHistoryTransition
{
    public static GraphHistoryTransitionResult Apply(
        GraphEditorModule module,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        try
        {
            GraphHistoryData data = GraphHistoryData.Decode(change.payload.ReadBytes());
            if (!module.TryResolve(data.documentId, out GraphDocumentSession? session) || session is null)
            {
                return new GraphHistoryTransitionResult(
                    EditorHistoryResult.Failure($"Graph document '{data.documentId}' is not open."),
                    false);
            }

            GraphDocument original = session.document.Clone();
            try
            {
                session.document.ReplaceContents(GraphHistoryDocumentCodec.Decode(
                    direction == EditorHistoryDirection.Undo ? data.before : data.after));
                session.revision++;
                session.isDirty = true;
                return new GraphHistoryTransitionResult(EditorHistoryResult.Success(), false);
            }
            catch (Exception exception)
            {
                try
                {
                    session.document.ReplaceContents(original);
                }
                catch (Exception rollbackException)
                {
                    return new GraphHistoryTransitionResult(
                        EditorHistoryResult.Failure(
                            $"Graph transition failed: {exception.Message} Rollback failed: {rollbackException.Message}"),
                        true);
                }

                return new GraphHistoryTransitionResult(EditorHistoryResult.Failure(exception.Message), false);
            }
        }
        catch (Exception exception)
        {
            return new GraphHistoryTransitionResult(
                EditorHistoryResult.Failure($"Graph History payload is invalid: {exception.Message}"),
                false);
        }
    }
}

[EditorHistoryHandler(GraphHistoryData.C_KIND)]
internal sealed class GraphHistoryHandler(GraphEditorModule module) : EditorHistoryHandler
{
    protected override EditorHistoryAvailability Query(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        _ = context;
        _ = direction;
        try
        {
            GraphHistoryData data = GraphHistoryData.Decode(change.payload.ReadBytes());
            return module.TryResolve(data.documentId, out _)
                ? EditorHistoryAvailability.Available()
                : EditorHistoryAvailability.Unavailable($"Graph document '{data.documentId}' is not open.");
        }
        catch (Exception exception)
        {
            return EditorHistoryAvailability.Unavailable($"Graph History payload is invalid: {exception.Message}");
        }
    }

    protected override EditorHistoryResult Apply(
        EditorHistoryContext context,
        EditorHistoryChange change,
        EditorHistoryDirection direction)
    {
        _ = context;
        GraphHistoryTransitionResult transition = GraphHistoryTransition.Apply(module, change, direction);
        return transition.stateIntegrityLost
            ? StateIntegrityFailure(transition.result.message)
            : transition.result;
    }

    protected override bool TryMerge(
        EditorHistoryChange older,
        EditorHistoryChange newer,
        out EditorHistoryChange? merged)
    {
        merged = null;
        if (older.mergeKey is null || !StringComparer.Ordinal.Equals(older.mergeKey, newer.mergeKey))
        {
            return false;
        }

        try
        {
            GraphHistoryData previous = GraphHistoryData.Decode(older.payload.ReadBytes());
            GraphHistoryData current = GraphHistoryData.Decode(newer.payload.ReadBytes());
            if (!StringComparer.Ordinal.Equals(previous.documentId, current.documentId)
                || Stopwatch.GetElapsedTime(previous.timestamp, current.timestamp).TotalSeconds > 1.0)
            {
                return false;
            }

            var combined = new GraphHistoryData(
                previous.documentId,
                previous.before,
                current.after,
                current.timestamp);
            merged = new EditorHistoryChange(
                GraphHistoryData.C_KIND,
                EditorHistoryPayload.FromBytes(combined.Encode()),
                older.mergeKey);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
