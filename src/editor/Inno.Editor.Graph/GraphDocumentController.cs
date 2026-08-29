using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Editor.Interactions;

namespace Inno.Editor.Graph;

/// <summary>Contains a neutral copied node fragment suitable for graph clipboard operations.</summary>
public sealed class GraphClipboardData
{
    internal GraphClipboardData(GraphDocument fragment) => m_fragment = fragment;

    private readonly GraphDocument m_fragment;

    /// <summary>Gets the copied node count.</summary>
    public int nodeCount => m_fragment.nodes.Count;

    /// <summary>Gets the copied connection count whose endpoints both belong to the fragment.</summary>
    public int edgeCount => m_fragment.edges.Count;

    internal GraphDocument CloneFragment() => m_fragment.Clone();
}

/// <summary>
/// Applies neutral graph edits atomically and records every data mutation in reload-safe Editor History.
/// </summary>
public sealed class GraphDocumentController
{
    private readonly GraphDocumentSession m_session;
    private readonly IEditorHistory m_history;

    internal GraphDocumentController(GraphDocumentSession session, IEditorHistory history)
    {
        m_session = session;
        m_history = history;
    }

    /// <summary>Gets the stable project-relative document identity.</summary>
    public string documentId => m_session.documentId;

    /// <summary>Gets the mutable neutral document used by the current asset generation.</summary>
    public GraphDocument document => m_session.document;

    /// <summary>Gets the monotonic in-session content revision.</summary>
    public ulong revision => m_session.revision;

    /// <summary>Gets whether content changed after its last explicit saved marker.</summary>
    public bool isDirty => m_session.isDirty;

    /// <summary>Marks the current revision as saved without creating a data History entry.</summary>
    public void MarkSaved() => m_session.isDirty = false;

    /// <summary>Adds a node with a generated stable identity.</summary>
    /// <param name="definitionId">Stable node definition ID.</param>
    /// <param name="position">Initial graph-space position.</param>
    /// <param name="values">Optional neutral property values.</param>
    /// <returns>The new stable node identity.</returns>
    public GraphNodeId AddNode(
        string definitionId,
        GraphPosition position,
        IReadOnlyDictionary<string, GraphSerializedValue>? values = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        GraphNodeId id = NewNodeId();
        Mutate("Create Node", null, () =>
        {
            var node = new GraphNodeRecord(id, definitionId) { position = position };
            if (values is not null)
            {
                foreach ((string propertyId, GraphSerializedValue value) in values)
                {
                    node.SetValue(propertyId, value.Clone());
                }
            }

            document.AddNode(node);
        });
        return id;
    }

    /// <summary>Removes nodes and all incident connections as one structural edit.</summary>
    /// <param name="nodeIds">Nodes to remove.</param>
    public void RemoveNodes(IEnumerable<GraphNodeId> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        GraphNodeId[] ids = nodeIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        foreach (GraphNodeId id in ids)
        {
            RequireNode(id);
        }

        Mutate(ids.Length == 1 ? "Delete Node" : "Delete Nodes", null, () =>
        {
            foreach (GraphNodeId id in ids)
            {
                document.RemoveNode(id);
            }
        });
    }

    /// <summary>Moves one or more nodes and merges adjacent drag samples for the same stable selection.</summary>
    /// <param name="positions">Complete destination positions keyed by node identity.</param>
    public void MoveNodes(IReadOnlyDictionary<GraphNodeId, GraphPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        if (positions.Count == 0)
        {
            return;
        }

        foreach (GraphNodeId id in positions.Keys)
        {
            RequireNode(id);
        }

        string selection = string.Join(",", positions.Keys.Select(static value => value.value).Order(StringComparer.Ordinal));
        Mutate(
            positions.Count == 1 ? "Move Node" : "Move Nodes",
            $"graph:{documentId}:move:{selection}",
            () =>
            {
                foreach ((GraphNodeId id, GraphPosition position) in positions)
                {
                    document.FindNode(id)!.position = position;
                }
            });
    }

    /// <summary>Creates or reconnects one input endpoint to an output endpoint.</summary>
    /// <param name="output">Source endpoint.</param>
    /// <param name="input">Destination endpoint.</param>
    /// <returns>The generated connection identity.</returns>
    public GraphEdgeId Connect(GraphEndpoint output, GraphEndpoint input)
    {
        RequireNode(output.nodeId);
        RequireNode(input.nodeId);
        if (output.nodeId == input.nodeId && output.portId == input.portId)
        {
            throw new ArgumentException("A graph endpoint cannot connect to itself.", nameof(input));
        }

        GraphEdgeId edgeId = NewEdgeId();
        Mutate("Connect Nodes", null, () =>
        {
            GraphEdgeId[] replaced = document.edges
                .Where(edge => edge.input == input)
                .Select(static edge => edge.id)
                .ToArray();
            foreach (GraphEdgeId existing in replaced)
            {
                document.RemoveEdge(existing);
            }

            document.AddEdge(new GraphEdgeRecord(edgeId, output, input));
        });
        return edgeId;
    }

    /// <summary>Removes one stable connection.</summary>
    /// <param name="edgeId">Connection to remove.</param>
    /// <returns><see langword="true"/> after a connection was removed.</returns>
    public bool Disconnect(GraphEdgeId edgeId)
    {
        if (!document.edges.Any(edge => edge.id == edgeId))
        {
            return false;
        }

        Mutate("Disconnect Nodes", null, () => document.RemoveEdge(edgeId));
        return true;
    }

    /// <summary>Creates or replaces one neutral node property and merges adjacent edits to that value.</summary>
    /// <param name="nodeId">Owning node.</param>
    /// <param name="propertyId">Stable property ID.</param>
    /// <param name="value">New neutral value.</param>
    public void SetNodeValue(GraphNodeId nodeId, string propertyId, GraphSerializedValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
        ArgumentNullException.ThrowIfNull(value);
        GraphNodeRecord node = RequireNode(nodeId);
        Mutate(
            "Edit Node Property",
            $"graph:{documentId}:value:{nodeId.value}:{propertyId}",
            () => node.SetValue(propertyId, value.Clone()));
    }

    /// <summary>Removes one neutral node property as a structural edit.</summary>
    /// <param name="nodeId">Owning node.</param>
    /// <param name="propertyId">Stable property ID.</param>
    /// <returns><see langword="true"/> when the property existed.</returns>
    public bool RemoveNodeValue(GraphNodeId nodeId, string propertyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
        GraphNodeRecord node = RequireNode(nodeId);
        if (!node.values.ContainsKey(propertyId))
        {
            return false;
        }

        Mutate("Remove Node Property", null, () => node.RemoveValue(propertyId));
        return true;
    }

    /// <summary>Copies selected nodes and only their internal connections.</summary>
    /// <param name="nodeIds">Nodes to copy.</param>
    /// <returns>A detached neutral clipboard fragment.</returns>
    public GraphClipboardData Copy(IEnumerable<GraphNodeId> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        HashSet<GraphNodeId> selected = [.. nodeIds];
        var fragment = new GraphDocument();
        foreach (GraphNodeRecord source in document.nodes.Where(node => selected.Contains(node.id)))
        {
            fragment.AddNode(CloneNode(source, source.id, source.position));
        }

        foreach (GraphEdgeRecord edge in document.edges.Where(
            edge => selected.Contains(edge.output.nodeId) && selected.Contains(edge.input.nodeId)))
        {
            fragment.AddEdge(new GraphEdgeRecord(edge.id, edge.output, edge.input));
        }

        return new GraphClipboardData(fragment);
    }

    /// <summary>Pastes a clipboard fragment with new stable identities and a graph-space offset.</summary>
    /// <param name="clipboard">Detached neutral fragment.</param>
    /// <param name="offset">Offset added to every copied node position.</param>
    /// <returns>New node identities in clipboard document order.</returns>
    public IReadOnlyList<GraphNodeId> Paste(GraphClipboardData clipboard, GraphPosition offset)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        GraphDocument fragment = clipboard.CloneFragment();
        Dictionary<GraphNodeId, GraphNodeId> remap = [];
        foreach (GraphNodeRecord node in fragment.nodes)
        {
            remap.Add(node.id, NewNodeId());
        }

        Mutate("Paste Nodes", null, () =>
        {
            foreach (GraphNodeRecord source in fragment.nodes)
            {
                document.AddNode(CloneNode(
                    source,
                    remap[source.id],
                    new GraphPosition(source.position.x + offset.x, source.position.y + offset.y)));
            }

            foreach (GraphEdgeRecord edge in fragment.edges)
            {
                document.AddEdge(new GraphEdgeRecord(
                    NewEdgeId(),
                    new GraphEndpoint(remap[edge.output.nodeId], edge.output.portId),
                    new GraphEndpoint(remap[edge.input.nodeId], edge.input.portId)));
            }
        });
        return fragment.nodes.Select(node => remap[node.id]).ToArray();
    }

    private void Mutate(string name, string? mergeKey, Action mutation)
    {
        if (!m_session.isOpen)
        {
            throw new InvalidOperationException($"Graph document '{documentId}' is closed.");
        }

        byte[] before = GraphHistoryDocumentCodec.Encode(document);
        try
        {
            mutation();
            byte[] after = GraphHistoryDocumentCodec.Encode(document);
            if (before.AsSpan().SequenceEqual(after))
            {
                return;
            }

            var change = GraphHistoryData.CreateChange(documentId, before, after, mergeKey);
            try
            {
                m_history.RecordApplied(name, change);
            }
            catch
            {
                change.Dispose();
                throw;
            }

            m_session.revision++;
            m_session.isDirty = true;
        }
        catch
        {
            document.ReplaceContents(GraphHistoryDocumentCodec.Decode(before));
            throw;
        }
    }

    private GraphNodeRecord RequireNode(GraphNodeId nodeId)
        => document.FindNode(nodeId)
            ?? throw new ArgumentException($"Node '{nodeId}' is not present in '{documentId}'.", nameof(nodeId));

    private static GraphNodeRecord CloneNode(
        GraphNodeRecord source,
        GraphNodeId id,
        GraphPosition position)
    {
        var clone = new GraphNodeRecord(id, source.definitionId) { position = position };
        foreach ((string propertyId, GraphSerializedValue value) in source.values)
        {
            clone.SetValue(propertyId, value.Clone());
        }

        return clone;
    }

    private static GraphNodeId NewNodeId() => new(Guid.NewGuid().ToString("N"));
    private static GraphEdgeId NewEdgeId() => new(Guid.NewGuid().ToString("N"));
}
