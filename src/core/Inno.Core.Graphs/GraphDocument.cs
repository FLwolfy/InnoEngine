using System;
using System.Collections.Generic;

namespace Inno.Core.Graphs;

/// <summary>
/// Stores one node without retaining a CLR type or runtime extension instance.
/// </summary>
public sealed class GraphNodeRecord
{
    private readonly Dictionary<string, GraphSerializedValue> m_values = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a graph node record.
    /// </summary>
    /// <param name="id">
    /// Stable identifier within the document.
    /// </param>
    /// <param name="definitionId">
    /// Stable node definition identifier.
    /// </param>
    public GraphNodeRecord(GraphNodeId id, string definitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        this.id = id;
        this.definitionId = definitionId;
    }

    /// <summary>
    /// Gets the stable node identifier.
    /// </summary>
    public GraphNodeId id { get; }

    /// <summary>
    /// Gets the stable node definition identifier.
    /// </summary>
    public string definitionId { get; }

    /// <summary>
    /// Gets or sets the graph-space node position.
    /// </summary>
    public GraphPosition position { get; set; }

    /// <summary>
    /// Gets neutral serialized property values keyed by stable property identifier.
    /// </summary>
    public IReadOnlyDictionary<string, GraphSerializedValue> values => m_values;

    /// <summary>
    /// Creates or replaces a neutral serialized property value.
    /// </summary>
    /// <param name="propertyId">
    /// Stable property identifier.
    /// </param>
    /// <param name="value">
    /// Serialized value.
    /// </param>
    public void SetValue(string propertyId, GraphSerializedValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
        ArgumentNullException.ThrowIfNull(value);
        m_values[propertyId] = value;
    }

    /// <summary>
    /// Tries to read a neutral serialized property value.
    /// </summary>
    /// <param name="propertyId">
    /// Stable property identifier.
    /// </param>
    /// <param name="value">
    /// Receives the value when present.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the property exists; otherwise <see langword="false"/>.
    /// </returns>
    public bool TryGetValue(string propertyId, out GraphSerializedValue? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
        return m_values.TryGetValue(propertyId, out value);
    }

    /// <summary>
    /// Removes a neutral serialized property value.
    /// </summary>
    /// <param name="propertyId">
    /// Stable property identifier.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a property was removed; otherwise <see langword="false"/>.
    /// </returns>
    public bool RemoveValue(string propertyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
        return m_values.Remove(propertyId);
    }
}

/// <summary>
/// Identifies one endpoint of a graph edge.
/// </summary>
public readonly record struct GraphEndpoint
{
    /// <summary>
    /// Creates a graph edge endpoint.
    /// </summary>
    /// <param name="nodeId">
    /// Owning node identifier.
    /// </param>
    /// <param name="portId">
    /// Port identifier within the node definition.
    /// </param>
    public GraphEndpoint(GraphNodeId nodeId, GraphPortId portId)
    {
        this.nodeId = nodeId;
        this.portId = portId;
    }

    /// <summary>
    /// Gets the owning node identifier.
    /// </summary>
    public GraphNodeId nodeId { get; }

    /// <summary>
    /// Gets the node-local port identifier.
    /// </summary>
    public GraphPortId portId { get; }
}

/// <summary>
/// Stores one typed connection between two graph endpoints.
/// </summary>
public sealed class GraphEdgeRecord
{
    /// <summary>
    /// Creates a graph edge record.
    /// </summary>
    /// <param name="id">
    /// Stable edge identifier within the document.
    /// </param>
    /// <param name="output">
    /// Source endpoint.
    /// </param>
    /// <param name="input">
    /// Destination endpoint.
    /// </param>
    public GraphEdgeRecord(GraphEdgeId id, GraphEndpoint output, GraphEndpoint input)
    {
        this.id = id;
        this.output = output;
        this.input = input;
    }

    /// <summary>
    /// Gets the stable edge identifier.
    /// </summary>
    public GraphEdgeId id { get; }

    /// <summary>
    /// Gets the source endpoint.
    /// </summary>
    public GraphEndpoint output { get; }

    /// <summary>
    /// Gets the destination endpoint.
    /// </summary>
    public GraphEndpoint input { get; }
}

/// <summary>
/// Owns neutral graph records and graph-level canvas metadata.
/// </summary>
public sealed class GraphDocument
{
    private readonly List<GraphNodeRecord> m_nodes = [];
    private readonly List<GraphEdgeRecord> m_edges = [];
    private readonly Dictionary<string, GraphSerializedValue> m_metadata = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets all node records in stable document order.
    /// </summary>
    public IReadOnlyList<GraphNodeRecord> nodes => m_nodes;

    /// <summary>
    /// Gets all edge records in stable document order.
    /// </summary>
    public IReadOnlyList<GraphEdgeRecord> edges => m_edges;

    /// <summary>
    /// Gets graph-level neutral metadata such as groups or comments.
    /// </summary>
    public IReadOnlyDictionary<string, GraphSerializedValue> metadata => m_metadata;

    /// <summary>
    /// Creates a deep neutral copy that shares no mutable node, edge, value, or metadata records.
    /// </summary>
    /// <returns>
    /// An independently mutable graph document with identical stable identifiers and order.
    /// </returns>
    public GraphDocument Clone()
    {
        var clone = new GraphDocument();
        clone.ReplaceContents(this);
        return clone;
    }

    /// <summary>
    /// Atomically replaces all records with deep copies from another neutral document.
    /// </summary>
    /// <param name="source">
    /// Complete source state.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="source"/> is this document.
    /// </exception>
    public void ReplaceContents(GraphDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(this, source))
        {
            throw new ArgumentException("A graph document cannot replace itself.", nameof(source));
        }

        List<GraphNodeRecord> nodes = [];
        foreach (GraphNodeRecord sourceNode in source.nodes)
        {
            var node = new GraphNodeRecord(sourceNode.id, sourceNode.definitionId)
            {
                position = sourceNode.position
            };
            foreach ((string propertyId, GraphSerializedValue value) in sourceNode.values)
            {
                node.SetValue(propertyId, value.Clone());
            }

            nodes.Add(node);
        }

        List<GraphEdgeRecord> edges = [];
        foreach (GraphEdgeRecord sourceEdge in source.edges)
        {
            edges.Add(new GraphEdgeRecord(sourceEdge.id, sourceEdge.output, sourceEdge.input));
        }

        Dictionary<string, GraphSerializedValue> metadata = new(StringComparer.Ordinal);
        foreach ((string key, GraphSerializedValue value) in source.metadata)
        {
            metadata.Add(key, value.Clone());
        }

        m_nodes.Clear();
        m_nodes.AddRange(nodes);
        m_edges.Clear();
        m_edges.AddRange(edges);
        m_metadata.Clear();
        foreach ((string key, GraphSerializedValue value) in metadata)
        {
            m_metadata.Add(key, value);
        }
    }

    /// <summary>
    /// Adds a node while preserving document order.
    /// </summary>
    /// <param name="node">
    /// Node record to add.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the node identifier already exists.
    /// </exception>
    public void AddNode(GraphNodeRecord node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (FindNode(node.id) is not null)
        {
            throw new ArgumentException($"Node '{node.id}' already exists.", nameof(node));
        }

        m_nodes.Add(node);
    }

    /// <summary>
    /// Removes a node and every edge connected to it.
    /// </summary>
    /// <param name="nodeId">
    /// Node identifier to remove.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the node existed; otherwise <see langword="false"/>.
    /// </returns>
    public bool RemoveNode(GraphNodeId nodeId)
    {
        int index = m_nodes.FindIndex(node => node.id == nodeId);
        if (index < 0)
        {
            return false;
        }

        m_nodes.RemoveAt(index);
        m_edges.RemoveAll(edge => edge.output.nodeId == nodeId || edge.input.nodeId == nodeId);
        return true;
    }

    /// <summary>
    /// Finds a node by stable identifier.
    /// </summary>
    /// <param name="nodeId">
    /// Node identifier to find.
    /// </param>
    /// <returns>
    /// The node record, or <see langword="null"/> when absent.
    /// </returns>
    public GraphNodeRecord? FindNode(GraphNodeId nodeId)
        => m_nodes.Find(node => node.id == nodeId);

    /// <summary>
    /// Adds an edge while preserving document order.
    /// </summary>
    /// <param name="edge">
    /// Edge record to add.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the edge identifier already exists.
    /// </exception>
    public void AddEdge(GraphEdgeRecord edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        if (m_edges.Exists(candidate => candidate.id == edge.id))
        {
            throw new ArgumentException($"Edge '{edge.id}' already exists.", nameof(edge));
        }

        m_edges.Add(edge);
    }

    /// <summary>
    /// Removes an edge by stable identifier.
    /// </summary>
    /// <param name="edgeId">
    /// Edge identifier to remove.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the edge existed; otherwise <see langword="false"/>.
    /// </returns>
    public bool RemoveEdge(GraphEdgeId edgeId)
    {
        int index = m_edges.FindIndex(edge => edge.id == edgeId);
        if (index < 0)
        {
            return false;
        }

        m_edges.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Creates or replaces graph-level neutral metadata.
    /// </summary>
    /// <param name="key">
    /// Stable metadata key.
    /// </param>
    /// <param name="value">
    /// Serialized metadata value.
    /// </param>
    public void SetMetadata(string key, GraphSerializedValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        m_metadata[key] = value;
    }

    /// <summary>
    /// Removes graph-level metadata by stable key.
    /// </summary>
    /// <param name="key">
    /// Stable metadata key.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when metadata existed and was removed.
    /// </returns>
    public bool RemoveMetadata(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return m_metadata.Remove(key);
    }
}
