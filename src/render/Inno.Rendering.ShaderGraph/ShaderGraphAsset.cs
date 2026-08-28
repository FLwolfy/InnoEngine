using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Inno.Assets.Core;
using Inno.Core.Graphs;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Rendering.ShaderGraph;

/// <summary>
/// Represents one imported neutral shader graph document and its target contract.
/// </summary>
[StableTypeId("2364a560-d890-4ec4-8ba7-5fa47404c140")]
public sealed class ShaderGraphAsset : ShaderAsset
{
    [SerializableProperty(PropertyVisibility.Hide)]
    private string m_documentJson = string.Empty;

    /// <summary>Gets the currently committed graph target.</summary>
    [SerializableProperty]
    public ShaderGraphTarget target { get; private set; }

    /// <summary>Gets the currently committed neutral graph document.</summary>
    public GraphDocument? document { get; private set; }

    internal void SetDocument(ShaderGraphTarget target, GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        this.target = target;
        this.document = document;
        m_documentJson = ShaderGraphDocumentCodec.Encode(target, document);
    }

    internal void CommitDefinition(ShaderDefinition definition)
        => SetDefinition(definition);

    [OnSerializableRestored]
    private void OnSerializableRestored()
    {
        if (string.IsNullOrWhiteSpace(m_documentJson))
        {
            document = null;
            return;
        }

        ShaderGraphDocumentData restored = ShaderGraphDocumentCodec.Decode(m_documentJson);
        target = restored.target;
        document = restored.document;
    }
}

/// <summary>
/// Contains one decoded shader graph source document.
/// </summary>
public sealed class ShaderGraphDocumentData
{
    /// <summary>Creates decoded shader graph data.</summary>
    /// <param name="target">Graph output target.</param>
    /// <param name="document">Neutral graph document.</param>
    public ShaderGraphDocumentData(ShaderGraphTarget target, GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        this.target = target;
        this.document = document;
    }

    /// <summary>Gets the graph output target.</summary>
    public ShaderGraphTarget target { get; }

    /// <summary>Gets the neutral graph document.</summary>
    public GraphDocument document { get; }
}

/// <summary>
/// Reads and writes the strict current shader graph JSON format without CLR type names.
/// </summary>
public static class ShaderGraphDocumentCodec
{
    private static readonly HashSet<string> S_ROOT_PROPERTIES = new(
        ["target", "nodes", "edges", "metadata"],
        StringComparer.Ordinal);
    private static readonly HashSet<string> S_NODE_PROPERTIES = new(
        ["id", "definition", "position", "values"],
        StringComparer.Ordinal);
    private static readonly HashSet<string> S_EDGE_PROPERTIES = new(
        ["id", "output", "input"],
        StringComparer.Ordinal);
    private static readonly HashSet<string> S_ENDPOINT_PROPERTIES = new(
        ["node", "port"],
        StringComparer.Ordinal);

    /// <summary>Encodes one current graph document as deterministic JSON.</summary>
    /// <param name="target">Graph output target.</param>
    /// <param name="document">Neutral graph document.</param>
    /// <returns>Deterministic current-format JSON.</returns>
    public static string Encode(ShaderGraphTarget target, GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("target", target.ToString());
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
                    using JsonDocument valueDocument = JsonDocument.Parse(value.json);
                    valueDocument.RootElement.WriteTo(writer);
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
                using JsonDocument valueDocument = JsonDocument.Parse(value.json);
                valueDocument.RootElement.WriteTo(writer);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Decodes strict current-format graph JSON with comments and trailing commas enabled.</summary>
    /// <param name="json">Shader graph source JSON.</param>
    /// <returns>The decoded target and neutral graph document.</returns>
    /// <exception cref="JsonException">Thrown for malformed or unknown schema members.</exception>
    public static ShaderGraphDocumentData Decode(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using JsonDocument source = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        JsonElement root = RequireObject(source.RootElement, "$", S_ROOT_PROPERTIES);
        ShaderGraphTarget target = Enum.Parse<ShaderGraphTarget>(
            root.GetProperty("target").GetString()
                ?? throw new JsonException("$.target must be a string."),
            ignoreCase: false);
        var document = new GraphDocument();
        foreach (JsonElement nodeElement in root.GetProperty("nodes").EnumerateArray())
        {
            JsonElement nodeObject = RequireObject(nodeElement, "$.nodes[]", S_NODE_PROPERTIES);
            var node = new GraphNodeRecord(
                new GraphNodeId(RequireString(nodeObject, "id", "$.nodes[].id")),
                RequireString(nodeObject, "definition", "$.nodes[].definition"));
            JsonElement position = nodeObject.GetProperty("position");
            if (position.ValueKind != JsonValueKind.Array || position.GetArrayLength() != 2)
            {
                throw new JsonException("$.nodes[].position must contain two numbers.");
            }

            node.position = new GraphPosition(position[0].GetSingle(), position[1].GetSingle());
            JsonElement values = nodeObject.GetProperty("values");
            if (values.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("$.nodes[].values must be an object.");
            }

            foreach (JsonProperty property in values.EnumerateObject())
            {
                node.SetValue(property.Name, new GraphSerializedValue(property.Value.GetRawText()));
            }

            document.AddNode(node);
        }

        foreach (JsonElement edgeElement in root.GetProperty("edges").EnumerateArray())
        {
            JsonElement edgeObject = RequireObject(edgeElement, "$.edges[]", S_EDGE_PROPERTIES);
            document.AddEdge(new GraphEdgeRecord(
                new GraphEdgeId(RequireString(edgeObject, "id", "$.edges[].id")),
                ReadEndpoint(edgeObject.GetProperty("output"), "$.edges[].output"),
                ReadEndpoint(edgeObject.GetProperty("input"), "$.edges[].input")));
        }

        JsonElement metadata = root.GetProperty("metadata");
        if (metadata.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("$.metadata must be an object.");
        }

        foreach (JsonProperty property in metadata.EnumerateObject())
        {
            document.SetMetadata(property.Name, new GraphSerializedValue(property.Value.GetRawText()));
        }

        return new ShaderGraphDocumentData(target, document);
    }

    private static JsonElement RequireObject(
        JsonElement element,
        string path,
        IReadOnlySet<string> allowedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"{path} must be an object.");
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name))
            {
                throw new JsonException($"{path}.{property.Name} is not a current shader graph field.");
            }
        }

        return element;
    }

    private static string RequireString(JsonElement element, string propertyName, string path)
        => element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(property.GetString())
                ? property.GetString()!
                : throw new JsonException($"{path} must be a non-empty string.");

    private static GraphEndpoint ReadEndpoint(JsonElement element, string path)
    {
        JsonElement endpoint = RequireObject(element, path, S_ENDPOINT_PROPERTIES);
        return new GraphEndpoint(
            new GraphNodeId(RequireString(endpoint, "node", $"{path}.node")),
            new GraphPortId(RequireString(endpoint, "port", $"{path}.port")));
    }

    private static void WriteEndpoint(Utf8JsonWriter writer, string name, GraphEndpoint endpoint)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteString("node", endpoint.nodeId.value);
        writer.WriteString("port", endpoint.portId.value);
        writer.WriteEndObject();
    }
}
