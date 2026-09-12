using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Inno.Core.Diagnostics;
using Inno.Core.Execution;
using Inno.Core.Graphs;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>Owns an immutable compiler map; the common lowering algorithm never switches on concrete node identities.</summary>
public sealed class ShaderNodeCompilerCatalog
{
    private readonly Dictionary<string, IShaderNodeCompiler> m_compilers = new(StringComparer.Ordinal);
    internal IReadOnlyList<IShaderNodeCompiler> providers { get; }

    /// <summary>Validates a complete generation of node compiler registrations.</summary>
    /// <param name="compilers">Providers owned by the calling generation or composition scope.</param>
    public ShaderNodeCompilerCatalog(IEnumerable<IShaderNodeCompiler> compilers)
    {
        ArgumentNullException.ThrowIfNull(compilers);
        foreach (IShaderNodeCompiler compiler in compilers)
        {
            ArgumentNullException.ThrowIfNull(compiler);
            ArgumentException.ThrowIfNullOrWhiteSpace(compiler.definitionId);
            if (!m_compilers.TryAdd(compiler.definitionId, compiler)) throw new ArgumentException($"Duplicate shader node compiler '{compiler.definitionId}'.", nameof(compilers));
        }
        providers = Array.AsReadOnly(m_compilers.Values.ToArray());
        definitionIds = Array.AsReadOnly(m_compilers.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Gets stable registered definition identities, without exposing providers.</summary>
    public IReadOnlyList<string> definitionIds { get; }

    /// <summary>Describes current typed ports for editor presentation without exposing the compiler provider.</summary>
    /// <param name="node">Neutral node properties.</param>
    /// <param name="serialization">Owner converter registry.</param>
    /// <param name="context">Complete owner reference context.</param>
    /// <param name="source">Resolved frozen function module, or null.</param>
    /// <param name="implementationId">Selected source implementation.</param>
    /// <param name="input">Resolved stage input interface, or null.</param>
    /// <returns>A detached immutable typed port snapshot.</returns>
    public IReadOnlyList<ShaderNodePort> DescribePorts(GraphNodeRecord node, SerializationRegistry serialization,
        SerializationContext context, ShaderSourceModuleAnalysis? source = null, string implementationId = "",
        ShaderIrStageInput? input = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!m_compilers.TryGetValue(node.definitionId, out IShaderNodeCompiler? compiler))
            throw new InvalidOperationException($"Node compiler '{node.definitionId}' is unavailable.");
        return Array.AsReadOnly(compiler.GetPorts(new(node, serialization, context, source, implementationId, input)).ToArray());
    }

    /// <summary>
    /// Lowers an entire target-selected region, preserving stable topological/document order and all source calls.
    /// Missing definitions, stale ports and cycles fail without mutating graph records or dropping edges.
    /// </summary>
    /// <param name="request">Frozen region, outputs and resolved source/target data.</param>
    /// <param name="serialization">The current owner's native converter registry.</param>
    /// <param name="context">The complete owner reference/asset serialization context; never synthesized by this compiler.</param>
    /// <param name="cancellationToken">Cancellation between node invocations.</param>
    /// <returns>A detached typed region or precise graph diagnostics.</returns>
    public ShaderGraphLoweringResult Lower(ShaderGraphLoweringRequest request, SerializationRegistry serialization,
        SerializationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<ShaderGraphDiagnostic>();
        GraphNodeId? activeNode = null;
        try
        {
            IReadOnlyList<GraphNodeRecord> nodes = request.graph.nodes;
            var descriptions = new Dictionary<GraphNodeId, ShaderNodeDescriptionContext>();
            var ports = new Dictionary<GraphEndpoint, ShaderNodePort>();
            var nodePorts = new Dictionary<GraphNodeId, IReadOnlyList<ShaderNodePort>>();
            var nodeIndexes = new Dictionary<GraphNodeId, int>();
            for (int index = 0; index < nodes.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GraphNodeRecord node = nodes[index];
                activeNode = node.id;
                nodeIndexes.Add(node.id, index);
                if (!m_compilers.TryGetValue(node.definitionId, out IShaderNodeCompiler? compiler))
                {
                    Error("SHADER_NODE_COMPILER_MISSING", $"Node compiler '{node.definitionId}' is unavailable.", node.id);
                    continue;
                }
                request.sourceModules.TryGetValue(node.id, out ShaderSourceModuleAnalysis? source);
                request.stageInputs.TryGetValue(node.id, out ShaderIrStageInput? input);
                var description = new ShaderNodeDescriptionContext(node, serialization, context, source, request.implementationId, input);
                descriptions.Add(node.id, description);
                IReadOnlyList<ShaderNodePort> describedPorts = compiler.GetPorts(description).ToArray();
                nodePorts.Add(node.id, describedPorts);
                foreach (ShaderNodePort port in describedPorts)
                {
                    if (port is null || string.IsNullOrWhiteSpace(port.id) || port.type is null || port.type.id == "void" || !Enum.IsDefined(port.direction))
                        throw new InvalidOperationException($"Compiler '{node.definitionId}' returned an invalid port.");
                    if (!ports.TryAdd(new(node.id, new(port.id)), port))
                        throw new InvalidOperationException($"Compiler '{node.definitionId}' returned duplicate port '{port.id}'.");
                }
            }
            activeNode = null;
            if (diagnostics.Count != 0) return new(null, diagnostics);

            var connections = new Dictionary<GraphEndpoint, GraphEndpoint>();
            var nodeConnections = nodes.ToDictionary(static node => node.id, static _ => new List<(string port, GraphEndpoint source)>());
            var incoming = new int[nodes.Count];
            var following = Enumerable.Range(0, nodes.Count).Select(static _ => new List<int>()).ToArray();
            foreach (GraphEdgeRecord edge in request.graph.edges)
            {
                if (!ports.TryGetValue(edge.input, out ShaderNodePort? input) || input.direction != GraphPortDirection.Input)
                { Error("SHADER_GRAPH_PORT_MISSING", $"Connection '{edge.id}' has an unavailable input; it remains stored for explicit repair.", edge.input.nodeId, edge.input.portId.value); continue; }
                if (!ports.TryGetValue(edge.output, out ShaderNodePort? output) || output.direction != GraphPortDirection.Output)
                { Error("SHADER_GRAPH_PORT_MISSING", $"Connection '{edge.id}' has an unavailable output; it remains stored for explicit repair.", edge.output.nodeId, edge.output.portId.value); continue; }
                if (!input.type.IsEquivalentTo(output.type))
                { Error("SHADER_GRAPH_TYPE", $"Connection '{edge.id}' changed type or aggregate layout; implicit conversion and positional rebinding are forbidden.", edge.input.nodeId, edge.input.portId.value); continue; }
                if (!connections.TryAdd(edge.input, edge.output))
                { Error("SHADER_GRAPH_INPUT_CAPACITY", "A value input has more than one connection.", edge.input.nodeId, edge.input.portId.value); continue; }
                int target = nodeIndexes[edge.input.nodeId];
                nodeConnections[edge.input.nodeId].Add((edge.input.portId.value, edge.output));
                incoming[target]++;
                following[nodeIndexes[edge.output.nodeId]].Add(target);
            }
            foreach ((GraphEndpoint endpoint, ShaderNodePort port) in ports)
                if (port.direction == GraphPortDirection.Input && port.required && !connections.ContainsKey(endpoint))
                    Error("SHADER_GRAPH_INPUT_REQUIRED", "A required input is not connected.", endpoint.nodeId, endpoint.portId.value);
            foreach ((string name, GraphEndpoint endpoint) in request.outputs)
                if (!ports.TryGetValue(endpoint, out ShaderNodePort? port) || port.direction != GraphPortDirection.Output)
                    Error("SHADER_GRAPH_OUTPUT_MISSING", $"Region output '{name}' points to an unavailable output port.", endpoint.nodeId, endpoint.portId.value);
            if (diagnostics.Count != 0) return new(null, diagnostics);

            var ready = new SortedSet<int>(Enumerable.Range(0, nodes.Count).Where(index => incoming[index] == 0));
            var ordered = new List<int>();
            while (ready.Count != 0)
            {
                int index = ready.Min;
                ready.Remove(index);
                ordered.Add(index);
                foreach (int next in following[index]) if (--incoming[next] == 0) ready.Add(next);
            }
            if (ordered.Count != nodes.Count)
            {
                Error("SHADER_GRAPH_CYCLE", "Value connections contain a cycle; structured control flow must be represented by a control-flow operation.");
                return new(null, diagnostics);
            }
            var builder = new ShaderIrBuilder();
            var values = new Dictionary<GraphEndpoint, ShaderIrValue>();
            foreach (int index in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GraphNodeRecord node = nodes[index];
                activeNode = node.id;
                var inputs = new Dictionary<string, ShaderIrValue>(StringComparer.Ordinal);
                foreach ((string port, GraphEndpoint source) in nodeConnections[node.id]) inputs.Add(port, values[source]);
                IReadOnlyDictionary<string, ShaderIrValue> outputs = m_compilers[node.definitionId].Lower(new(descriptions[node.id], builder, inputs));
                ShaderNodePort[] expected = nodePorts[node.id].Where(static port => port.direction == GraphPortDirection.Output).ToArray();
                if (outputs.Count != expected.Length) throw new InvalidOperationException("A node compiler did not return exactly its declared outputs.");
                foreach (ShaderNodePort port in expected)
                {
                    if (!outputs.TryGetValue(port.id, out ShaderIrValue? value) || !port.type.IsEquivalentTo(value.type))
                        throw new InvalidOperationException($"Node output '{port.id}' is missing or has an incompatible type.");
                    builder.RequireOwned(value);
                    values.Add(new(node.id, new(port.id)), value);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(builder.Build(request.outputs.ToDictionary(static pair => pair.Key, pair => values[pair.Value], StringComparer.Ordinal)), diagnostics);
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or InvalidDataException or KeyNotFoundException
            or FormatException or NotSupportedException && RetirementPendingException.Find(failure) is null)
        {
            Error("SHADER_NODE_LOWERING", failure.Message, activeNode);
            return new(null, diagnostics);
        }

        void Error(string code, string message, GraphNodeId? nodeId = null, string? portId = null)
            => diagnostics.Add(new(code, DiagnosticSeverity.Error, message, nodeId, portId));
    }
}
