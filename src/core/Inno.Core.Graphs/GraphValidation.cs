using System;
using System.Collections.Generic;

namespace Inno.Core.Graphs;

/// <summary>
/// Indicates the impact of a graph validation diagnostic.
/// </summary>
public enum GraphDiagnosticSeverity
{
    /// <summary>Provides non-blocking information.</summary>
    Info,
    /// <summary>Preserves the graph but may reduce functionality.</summary>
    Warning,
    /// <summary>Prevents graph compilation or execution.</summary>
    Error
}

/// <summary>
/// Reports one graph validation problem using stable document identifiers.
/// </summary>
public sealed class GraphDiagnostic
{
    /// <summary>
    /// Creates a graph diagnostic.
    /// </summary>
    /// <param name="code">Stable machine-readable diagnostic code.</param>
    /// <param name="message">Artist-facing diagnostic text.</param>
    /// <param name="severity">Diagnostic impact.</param>
    /// <param name="nodeId">Optional related node identifier.</param>
    /// <param name="edgeId">Optional related edge identifier.</param>
    public GraphDiagnostic(
        string code,
        string message,
        GraphDiagnosticSeverity severity,
        GraphNodeId? nodeId = null,
        GraphEdgeId? edgeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        this.code = code;
        this.message = message;
        this.severity = severity;
        this.nodeId = nodeId;
        this.edgeId = edgeId;
    }

    /// <summary>Gets the stable machine-readable diagnostic code.</summary>
    public string code { get; }

    /// <summary>Gets the artist-facing diagnostic text.</summary>
    public string message { get; }

    /// <summary>Gets the diagnostic impact.</summary>
    public GraphDiagnosticSeverity severity { get; }

    /// <summary>Gets the related node identifier, if any.</summary>
    public GraphNodeId? nodeId { get; }

    /// <summary>Gets the related edge identifier, if any.</summary>
    public GraphEdgeId? edgeId { get; }
}

/// <summary>
/// Contains deterministic graph validation diagnostics.
/// </summary>
public sealed class GraphValidationResult
{
    private readonly IReadOnlyList<GraphDiagnostic> m_diagnostics;

    /// <summary>
    /// Creates a graph validation result.
    /// </summary>
    /// <param name="diagnostics">Diagnostics in deterministic validation order.</param>
    public GraphValidationResult(IReadOnlyList<GraphDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        m_diagnostics = diagnostics;
    }

    /// <summary>Gets diagnostics in deterministic validation order.</summary>
    public IReadOnlyList<GraphDiagnostic> diagnostics => m_diagnostics;

    /// <summary>Gets whether no error diagnostic is present.</summary>
    public bool isValid
    {
        get
        {
            foreach (GraphDiagnostic diagnostic in m_diagnostics)
            {
                if (diagnostic.severity == GraphDiagnosticSeverity.Error)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

/// <summary>
/// Validates neutral graph topology against generation-scoped node definitions.
/// </summary>
public static class GraphValidator
{
    /// <summary>
    /// Validates node availability, ports, edge types, capacity, required inputs and cycles.
    /// </summary>
    /// <param name="document">Graph document to validate without modifying it.</param>
    /// <param name="resolver">Active node definition resolver.</param>
    /// <param name="conversion">Optional directed type conversion policy.</param>
    /// <param name="allowCycles">Whether directed cycles are permitted.</param>
    /// <returns>A deterministic validation result. Missing definitions remain warnings so documents stay editable.</returns>
    public static GraphValidationResult Validate(
        GraphDocument document,
        IGraphNodeDefinitionResolver resolver,
        IGraphTypeConversion? conversion = null,
        bool allowCycles = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(resolver);

        List<GraphDiagnostic> diagnostics = [];
        Dictionary<GraphNodeId, IReadOnlyDictionary<GraphPortId, GraphPortDefinition>> portsByNode = [];

        ResolvePorts(document, resolver, portsByNode, diagnostics);
        ValidateEdges(document, portsByNode, conversion, diagnostics);
        ValidateRequiredInputs(document, portsByNode, diagnostics);

        if (!allowCycles)
        {
            ValidateCycles(document, diagnostics);
        }

        return new GraphValidationResult(diagnostics);
    }

    private static void ResolvePorts(
        GraphDocument document,
        IGraphNodeDefinitionResolver resolver,
        Dictionary<GraphNodeId, IReadOnlyDictionary<GraphPortId, GraphPortDefinition>> portsByNode,
        List<GraphDiagnostic> diagnostics)
    {
        foreach (GraphNodeRecord node in document.nodes)
        {
            if (!resolver.TryResolve(node.definitionId, out GraphNodeDefinition? definition) || definition is null)
            {
                diagnostics.Add(new GraphDiagnostic(
                    "GRAPH_MISSING_NODE",
                    $"Node definition '{node.definitionId}' is not active. The node and its connections were preserved.",
                    GraphDiagnosticSeverity.Warning,
                    node.id));
                continue;
            }

            try
            {
                Dictionary<GraphPortId, GraphPortDefinition> ports = [];
                foreach (GraphPortDefinition port in definition.GetPorts(node))
                {
                    if (!ports.TryAdd(port.id, port))
                    {
                        diagnostics.Add(new GraphDiagnostic(
                            "GRAPH_DUPLICATE_PORT",
                            $"Node definition '{node.definitionId}' returned duplicate port '{port.id}'.",
                            GraphDiagnosticSeverity.Error,
                            node.id));
                    }
                }

                portsByNode[node.id] = ports;
            }
            catch (Exception exception)
            {
                diagnostics.Add(new GraphDiagnostic(
                    "GRAPH_NODE_RESOLUTION_FAILED",
                    $"Node '{node.id}' could not resolve its ports: {exception.Message}",
                    GraphDiagnosticSeverity.Error,
                    node.id));
            }
        }
    }

    private static void ValidateEdges(
        GraphDocument document,
        IReadOnlyDictionary<GraphNodeId, IReadOnlyDictionary<GraphPortId, GraphPortDefinition>> portsByNode,
        IGraphTypeConversion? conversion,
        List<GraphDiagnostic> diagnostics)
    {
        Dictionary<GraphEndpoint, int> connectionCounts = [];

        foreach (GraphEdgeRecord edge in document.edges)
        {
            GraphNodeRecord? outputNode = document.FindNode(edge.output.nodeId);
            GraphNodeRecord? inputNode = document.FindNode(edge.input.nodeId);
            if (outputNode is null || inputNode is null)
            {
                diagnostics.Add(new GraphDiagnostic(
                    "GRAPH_MISSING_ENDPOINT_NODE",
                    $"Edge '{edge.id}' references a node that is not present in the document.",
                    GraphDiagnosticSeverity.Error,
                    edgeId: edge.id));
                continue;
            }

            if (!TryGetPort(portsByNode, edge.output, out GraphPortDefinition outputPort)
                || !TryGetPort(portsByNode, edge.input, out GraphPortDefinition inputPort))
            {
                if (portsByNode.ContainsKey(edge.output.nodeId) && portsByNode.ContainsKey(edge.input.nodeId))
                {
                    diagnostics.Add(new GraphDiagnostic(
                        "GRAPH_MISSING_ENDPOINT_PORT",
                        $"Edge '{edge.id}' references a port that is not active on its node.",
                        GraphDiagnosticSeverity.Error,
                        edgeId: edge.id));
                }

                continue;
            }

            if (outputPort.direction != GraphPortDirection.Output || inputPort.direction != GraphPortDirection.Input)
            {
                diagnostics.Add(new GraphDiagnostic(
                    "GRAPH_INVALID_DIRECTION",
                    $"Edge '{edge.id}' must connect an output port to an input port.",
                    GraphDiagnosticSeverity.Error,
                    edgeId: edge.id));
            }

            bool compatible = StringComparer.Ordinal.Equals(outputPort.valueTypeId, inputPort.valueTypeId)
                || (conversion?.CanConvert(outputPort.valueTypeId, inputPort.valueTypeId) ?? false);
            if (!compatible)
            {
                diagnostics.Add(new GraphDiagnostic(
                    "GRAPH_INCOMPATIBLE_TYPES",
                    $"Edge '{edge.id}' cannot convert '{outputPort.valueTypeId}' to '{inputPort.valueTypeId}'.",
                    GraphDiagnosticSeverity.Error,
                    edgeId: edge.id));
            }

            CountConnection(edge.output, outputPort, connectionCounts, edge.id, diagnostics);
            CountConnection(edge.input, inputPort, connectionCounts, edge.id, diagnostics);
        }
    }

    private static void CountConnection(
        GraphEndpoint endpoint,
        GraphPortDefinition port,
        Dictionary<GraphEndpoint, int> connectionCounts,
        GraphEdgeId edgeId,
        List<GraphDiagnostic> diagnostics)
    {
        connectionCounts.TryGetValue(endpoint, out int currentCount);
        currentCount++;
        connectionCounts[endpoint] = currentCount;
        if (port.capacity == GraphPortCapacity.Single && currentCount > 1)
        {
            diagnostics.Add(new GraphDiagnostic(
                "GRAPH_PORT_CAPACITY",
                $"Port '{endpoint.portId}' accepts only one connection.",
                GraphDiagnosticSeverity.Error,
                endpoint.nodeId,
                edgeId));
        }
    }

    private static void ValidateRequiredInputs(
        GraphDocument document,
        IReadOnlyDictionary<GraphNodeId, IReadOnlyDictionary<GraphPortId, GraphPortDefinition>> portsByNode,
        List<GraphDiagnostic> diagnostics)
    {
        HashSet<GraphEndpoint> connectedInputs = [];
        foreach (GraphEdgeRecord edge in document.edges)
        {
            connectedInputs.Add(edge.input);
        }

        foreach ((GraphNodeId nodeId, IReadOnlyDictionary<GraphPortId, GraphPortDefinition> ports) in portsByNode)
        {
            foreach (GraphPortDefinition port in ports.Values)
            {
                if (port.direction == GraphPortDirection.Input
                    && port.required
                    && !connectedInputs.Contains(new GraphEndpoint(nodeId, port.id)))
                {
                    diagnostics.Add(new GraphDiagnostic(
                        "GRAPH_REQUIRED_INPUT",
                        $"Required input '{port.displayName}' is not connected.",
                        GraphDiagnosticSeverity.Error,
                        nodeId));
                }
            }
        }
    }

    private static void ValidateCycles(GraphDocument document, List<GraphDiagnostic> diagnostics)
    {
        Dictionary<GraphNodeId, List<GraphNodeId>> adjacency = [];
        foreach (GraphNodeRecord node in document.nodes)
        {
            adjacency[node.id] = [];
        }

        foreach (GraphEdgeRecord edge in document.edges)
        {
            if (adjacency.TryGetValue(edge.output.nodeId, out List<GraphNodeId>? targets)
                && adjacency.ContainsKey(edge.input.nodeId))
            {
                targets.Add(edge.input.nodeId);
            }
        }

        HashSet<GraphNodeId> visiting = [];
        HashSet<GraphNodeId> visited = [];
        foreach (GraphNodeRecord node in document.nodes)
        {
            if (ContainsCycle(node.id, adjacency, visiting, visited))
            {
                diagnostics.Add(new GraphDiagnostic(
                    "GRAPH_CYCLE",
                    "The graph contains a directed cycle.",
                    GraphDiagnosticSeverity.Error,
                    node.id));
                return;
            }
        }
    }

    private static bool ContainsCycle(
        GraphNodeId nodeId,
        IReadOnlyDictionary<GraphNodeId, List<GraphNodeId>> adjacency,
        HashSet<GraphNodeId> visiting,
        HashSet<GraphNodeId> visited)
    {
        if (visited.Contains(nodeId))
        {
            return false;
        }

        if (!visiting.Add(nodeId))
        {
            return true;
        }

        foreach (GraphNodeId target in adjacency[nodeId])
        {
            if (ContainsCycle(target, adjacency, visiting, visited))
            {
                return true;
            }
        }

        visiting.Remove(nodeId);
        visited.Add(nodeId);
        return false;
    }

    private static bool TryGetPort(
        IReadOnlyDictionary<GraphNodeId, IReadOnlyDictionary<GraphPortId, GraphPortDefinition>> portsByNode,
        GraphEndpoint endpoint,
        out GraphPortDefinition port)
    {
        if (portsByNode.TryGetValue(
                endpoint.nodeId,
                out IReadOnlyDictionary<GraphPortId, GraphPortDefinition>? ports)
            && ports.TryGetValue(endpoint.portId, out GraphPortDefinition? resolved))
        {
            port = resolved;
            return true;
        }

        port = null!;
        return false;
    }
}
