using System;
using System.Collections.Generic;
using Inno.Core.Graphs;

namespace Inno.Editor.Graph;

/// <summary>Stores transient pan, zoom, selection and connection interaction state without ImGui dependencies.</summary>
public sealed class GraphCanvasState
{
    private readonly HashSet<GraphNodeId> m_selectedNodes = [];
    private readonly HashSet<GraphEdgeId> m_selectedEdges = [];

    /// <summary>Gets the graph-space origin mapped to canvas screen origin.</summary>
    public GraphPosition pan { get; private set; }

    /// <summary>Gets the current graph-to-screen scale.</summary>
    public float zoom { get; private set; } = 1f;

    /// <summary>Gets selected node identities.</summary>
    public IReadOnlyCollection<GraphNodeId> selectedNodes => m_selectedNodes;

    /// <summary>Gets selected edge identities.</summary>
    public IReadOnlyCollection<GraphEdgeId> selectedEdges => m_selectedEdges;

    /// <summary>Gets the output endpoint currently being connected, or <see langword="null"/>.</summary>
    public GraphEndpoint? pendingConnection { get; private set; }

    /// <summary>Restores persistent canvas navigation state with bounded zoom.</summary>
    /// <param name="pan">Graph-space pan offset.</param>
    /// <param name="zoom">Requested zoom in the inclusive range 0.1 to 4.</param>
    public void SetViewport(GraphPosition pan, float zoom)
    {
        if (!float.IsFinite(zoom))
        {
            throw new ArgumentOutOfRangeException(nameof(zoom));
        }

        this.pan = pan;
        this.zoom = Math.Clamp(zoom, 0.1f, 4f);
    }

    /// <summary>Moves the canvas origin by screen-space pixels.</summary>
    /// <param name="deltaX">Horizontal screen-space delta.</param>
    /// <param name="deltaY">Vertical screen-space delta.</param>
    public void PanBy(float deltaX, float deltaY)
        => pan = new GraphPosition(pan.x + deltaX, pan.y + deltaY);

    /// <summary>Changes zoom while preserving the graph point beneath a screen-space pivot.</summary>
    /// <param name="factor">Positive multiplicative zoom factor.</param>
    /// <param name="pivotX">Screen-space pivot X.</param>
    /// <param name="pivotY">Screen-space pivot Y.</param>
    public void ZoomAt(float factor, float pivotX, float pivotY)
    {
        if (!float.IsFinite(factor) || factor <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(factor));
        }

        float next = Math.Clamp(zoom * factor, 0.1f, 4f);
        float graphX = (pivotX - pan.x) / zoom;
        float graphY = (pivotY - pan.y) / zoom;
        pan = new GraphPosition(pivotX - (graphX * next), pivotY - (graphY * next));
        zoom = next;
    }

    /// <summary>Replaces node selection.</summary>
    /// <param name="nodes">Complete selected-node set.</param>
    public void SelectNodes(IEnumerable<GraphNodeId> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        m_selectedNodes.Clear();
        m_selectedNodes.UnionWith(nodes);
    }

    /// <summary>Toggles one node while preserving the remaining selection.</summary>
    /// <param name="nodeId">Node to toggle.</param>
    public void ToggleNode(GraphNodeId nodeId)
    {
        if (!m_selectedNodes.Remove(nodeId))
        {
            m_selectedNodes.Add(nodeId);
        }
    }

    /// <summary>Clears all transient node and edge selection.</summary>
    public void ClearSelection()
    {
        m_selectedNodes.Clear();
        m_selectedEdges.Clear();
    }

    /// <summary>Begins a connection drag from one output endpoint.</summary>
    /// <param name="output">Stable output endpoint.</param>
    public void BeginConnection(GraphEndpoint output) => pendingConnection = output;

    /// <summary>Cancels any active connection drag.</summary>
    public void CancelConnection() => pendingConnection = null;
}
