using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Inno.Core.Diagnostics;
using Inno.Core.Execution;
using Inno.Core.Graphs;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>Partitions explicit GPU-stage regions and lowers them through registered node compilers.</summary>
public sealed class ShaderGraphProgramCompiler
{
    private readonly ShaderNodeCompilerRegistry m_nodes;

    /// <summary>Uses the shared node compiler generation rather than retaining individual providers.</summary>
    /// <param name="nodes">The owner-scoped node registry.</param>
    public ShaderGraphProgramCompiler(ShaderNodeCompilerRegistry nodes)
        => m_nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));

    /// <summary>Validates the complete pass/stage graph without deleting invalid or unavailable records.</summary>
    /// <param name="document">Authored graph, copied before validation.</param>
    /// <param name="implementationId">Exact adapter implementation identity selected by the caller.</param>
    /// <param name="sources">Frozen function modules keyed by source node identity.</param>
    /// <param name="serialization">The owner converter registry.</param>
    /// <param name="context">The complete owner reference context.</param>
    /// <param name="cancellationToken">Cancellation between stages and node invocations.</param>
    /// <returns>All typed passes, or diagnostics without a partial publishable program.</returns>
    public ShaderGraphProgramResult Lower(GraphDocument document, string implementationId,
        IReadOnlyDictionary<GraphNodeId, ShaderSourceModuleAnalysis> sources,
        SerializationRegistry serialization, SerializationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sources);
        var diagnostics = new List<ShaderGraphDiagnostic>();
        var passes = new List<ShaderGraphPass>();
        GraphNodeId? activeNode = null;
        try
        {
            GraphDocument graph = document.Clone();
            ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(graph, serialization, context);
            diagnostics.AddRange(ShaderDefinitionValidator.Validate(definition).Select(static value =>
                new ShaderGraphDiagnostic(value.code, value.severity, value.message)));
            if (diagnostics.Any(static value => value.severity == DiagnosticSeverity.Error)) return new([], diagnostics);
            ConnectRasterStages(graph, implementationId, sources, serialization, context);
            var definitions = definition.passes.ToDictionary(static pass => pass.name, StringComparer.Ordinal);
            if (definitions.Count == 0) throw new InvalidOperationException("A shader graph requires at least one pass.");
            ShaderGraphPassProgram[] programs = ShaderGraphPrograms.Read(graph, serialization, context);
            if (programs.Select(static value => value.pass).Distinct(StringComparer.Ordinal).Count() != programs.Length
                || programs.Any(value => !definitions.ContainsKey(value.pass)))
                throw new InvalidOperationException("Program references must name unique declared passes.");
            var stages = new Dictionary<string, ShaderIrStage>(StringComparer.Ordinal);
            var outputs = graph.nodes.Where(static node => node.definitionId == ShaderGraphDocument.outputDefinitionId).ToArray();
            var owners = graph.nodes.Where(static node => node.definitionId != ShaderGraphDocument.outputDefinitionId)
                .ToDictionary(static node => node.id, node => ShaderGraphDocument.Read(node, ShaderGraphDocument.stageKey, "", serialization, context));
            var outputIds = outputs.Select(static node => node.id.value).ToHashSet(StringComparer.Ordinal);
            foreach ((GraphNodeId node, string owner) in owners)
                if (!outputIds.Contains(owner)) Error("SHADER_STAGE_MISSING", "The node's stage is missing; its data remains available for repair.", node);
            if (diagnostics.Count != 0) return new([], diagnostics);
            foreach (GraphNodeRecord output in outputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                activeNode = output.id;
                ShaderGraphStageSettings settings = ShaderGraphDocument.Read<ShaderGraphStageSettings?>(output,
                    ShaderGraphDocument.settingsKey, null, serialization, context)
                    ?? throw new InvalidOperationException("A stage output node requires its stage settings.");
                var region = graph.Clone();
                foreach (GraphNodeRecord node in graph.nodes)
                    if (!owners.TryGetValue(node.id, out string? owner) || owner != output.id.value) region.RemoveNode(node.id);
                HashSet<GraphNodeId> members = region.nodes.Select(static node => node.id).ToHashSet();
                var endpoints = new Dictionary<string, GraphEndpoint>(StringComparer.Ordinal);
                foreach (GraphEdgeRecord edge in graph.edges)
                {
                    if (edge.input.nodeId == output.id)
                    {
                        if (!members.Contains(edge.output.nodeId)) throw new InvalidOperationException("A stage output is connected to another stage; use an explicit varying interface.");
                        if (!endpoints.TryAdd(edge.input.portId.value, edge.output)) throw new InvalidOperationException("A stage output port has multiple incoming connections.");
                    }
                    else if (members.Contains(edge.input.nodeId) != members.Contains(edge.output.nodeId)
                        && edge.output.nodeId != output.id && edge.input.nodeId != output.id)
                        throw new InvalidOperationException("A connection crosses incompatible stage boundaries.");
                    if (edge.output.nodeId == output.id) throw new InvalidOperationException("A GPU stage output node cannot produce graph values.");
                }
                string[] expected = settings.outputs.Select(static value => value.id).ToArray();
                if (expected.Distinct(StringComparer.Ordinal).Count() != expected.Length || !expected.ToHashSet(StringComparer.Ordinal).SetEquals(endpoints.Keys))
                    throw new InvalidOperationException("Every stage output must have exactly one connection with its original port identity.");
                var inputs = region.nodes.Where(static node => node.definitionId == "inno.shader.stage-input")
                    .ToDictionary(static node => node.id, node =>
                        (ShaderGraphDocument.Read<ShaderGraphInputSettings?>(node, ShaderGraphDocument.settingsKey, null, serialization, context)
                         ?? throw new InvalidOperationException("A stage input requires its interface settings.")).CreateBinding());
                IGrouping<string, KeyValuePair<GraphNodeId, ShaderIrStageInput>>? duplicateInput = inputs
                    .GroupBy(static pair => pair.Value.id, StringComparer.Ordinal)
                    .FirstOrDefault(static group => group.Count() > 1);
                if (duplicateInput is not null)
                {
                    string nodes = string.Join(", ", duplicateInput.Select(static pair => $"'{pair.Key.value}'"));
                    throw new InvalidOperationException(
                        $"Stage '{output.id.value}' contains duplicate input '{duplicateInput.Key}' from graph nodes {nodes}.");
                }
                ShaderGraphLoweringResult lowered = m_nodes.Lower(new(region, endpoints, implementationId,
                    sources.Where(pair => members.Contains(pair.Key)).ToDictionary(), inputs), serialization, context, cancellationToken);
                diagnostics.AddRange(lowered.diagnostics);
                if (!lowered.succeeded) continue;
                var stage = new ShaderIrStage(settings.stage, lowered.block!, inputs.Values,
                    settings.outputs.Select(static value => new ShaderIrStageOutput(value.id, value.kind, value.semantic ?? "", value.location)),
                    settings.threadsX, settings.threadsY, settings.threadsZ);
                stages.Add(output.id.value, stage);
            }
            activeNode = null;
            foreach (ShaderPassDefinition pass in definition.passes)
            {
                ShaderStage[] expected = pass.programKind switch
                {
                    ShaderProgramKind.Raster => [ShaderStage.Vertex, ShaderStage.Fragment],
                    ShaderProgramKind.Compute => [ShaderStage.Compute],
                    _ => throw new InvalidOperationException("The pass program kind is invalid.")
                };
                ShaderGraphPassProgram[] references = programs.Where(value => value.pass == pass.name).ToArray();
                if (references.Length != 1 || references[0].stages is null)
                { Error("SHADER_PROGRAM_MISSING", $"Pass '{pass.name}' has no stage program assignment."); continue; }
                var selected = new Dictionary<ShaderStage, ShaderIrStage>();
                foreach (string reference in references[0].stages)
                {
                    if (!stages.TryGetValue(reference, out ShaderIrStage? shared))
                    { Error("SHADER_PROGRAM_STAGE_MISSING", $"Pass '{pass.name}' refers to unavailable stage program '{reference}'."); continue; }
                    if (!selected.TryAdd(shared.stage, shared))
                        Error("SHADER_PROGRAM_STAGE_DUPLICATE", $"Pass '{pass.name}' repeats stage {shared.stage}.");
                }
                if (!expected.ToHashSet().SetEquals(selected.Keys))
                { Error("SHADER_STAGE_SET", $"Pass '{pass.name}' requires exactly {string.Join(" and ", expected)} stages."); continue; }
                if (pass.programKind == ShaderProgramKind.Raster) ValidateVaryings(selected[ShaderStage.Vertex], selected[ShaderStage.Fragment]);
                passes.Add(new(pass.name, expected.Select(stage => selected[stage])));
            }
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or FormatException or NotSupportedException
            && RetirementPendingException.Find(failure) is null)
        { Error("SHADER_GRAPH_PROGRAM", failure.Message, activeNode); }
        return new(diagnostics.Any(static value => value.severity == DiagnosticSeverity.Error) ? [] : passes, diagnostics);

        void Error(string code, string message, GraphNodeId? node = null)
            => diagnostics.Add(new(code, DiagnosticSeverity.Error, message, node));
    }

    private void ConnectRasterStages(GraphDocument graph, string implementationId,
        IReadOnlyDictionary<GraphNodeId, ShaderSourceModuleAnalysis> sources,
        SerializationRegistry serialization, SerializationContext context)
    {
        GraphNodeRecord[] outputs = graph.nodes
            .Where(static node => node.definitionId == ShaderGraphDocument.outputDefinitionId)
            .ToArray();
        var outputById = outputs.ToDictionary(static node => node.id.value, StringComparer.Ordinal);
        var stageById = outputs.ToDictionary(
            static node => node.id.value,
            node => ShaderGraphDocument.Read(node, ShaderGraphDocument.settingsKey,
                new ShaderGraphStageSettings(), serialization, context).stage,
            StringComparer.Ordinal);

        string Owner(GraphNodeRecord node)
            => node.definitionId == ShaderGraphDocument.outputDefinitionId
                ? node.id.value
                : ShaderGraphDocument.Read(node, ShaderGraphDocument.stageKey, "", serialization, context);

        var crossings = graph.edges.Select(edge =>
            {
                GraphNodeRecord source = graph.FindNode(edge.output.nodeId)
                    ?? throw new InvalidOperationException($"Connection '{edge.id.value}' has no source node.");
                GraphNodeRecord destination = graph.FindNode(edge.input.nodeId)
                    ?? throw new InvalidOperationException($"Connection '{edge.id.value}' has no destination node.");
                return (edge, source, destination, sourceOwner: Owner(source), destinationOwner: Owner(destination));
            })
            .Where(static value => value.sourceOwner.Length != 0 && value.destinationOwner.Length != 0
                && value.sourceOwner != value.destinationOwner)
            .OrderBy(static value => value.sourceOwner, StringComparer.Ordinal)
            .ThenBy(static value => value.edge.output.nodeId.value, StringComparer.Ordinal)
            .ThenBy(static value => value.edge.output.portId.value, StringComparer.Ordinal)
            .ThenBy(static value => value.destinationOwner, StringComparer.Ordinal)
            .ThenBy(static value => value.edge.input.nodeId.value, StringComparer.Ordinal)
            .ThenBy(static value => value.edge.input.portId.value, StringComparer.Ordinal)
            .ToArray();
        if (crossings.Length == 0) return;

        var ports = new Dictionary<GraphEndpoint, ShaderNodePort>();
        foreach (GraphNodeRecord node in graph.nodes.Where(static node => node.definitionId != ShaderGraphDocument.outputDefinitionId))
        {
            sources.TryGetValue(node.id, out ShaderSourceModuleAnalysis? source);
            ShaderIrStageInput? input = node.definitionId == "inno.shader.stage-input"
                ? ShaderGraphDocument.Read(node, ShaderGraphDocument.settingsKey,
                    new ShaderGraphInputSettings(), serialization, context).CreateBinding()
                : null;
            foreach (ShaderNodePort port in m_nodes.DescribePorts(node, serialization, context, source, implementationId, input))
                ports.Add(new(node.id, new(port.id)), port);
        }

        var occupiedLocations = outputs.ToDictionary(
            static node => node.id.value,
            node => ShaderGraphDocument.Read(node, ShaderGraphDocument.settingsKey,
                    new ShaderGraphStageSettings(), serialization, context).outputs
                .Where(static output => output.kind == ShaderIrOutputKind.Varying
                    && string.Equals(output.semantic, "texcoord", StringComparison.Ordinal))
                .Select(static output => output.location)
                .ToHashSet(),
            StringComparer.Ordinal);
        var sourceBridges = new Dictionary<GraphEndpoint, (string portId, int location)>();
        var destinationBridges = new Dictionary<(GraphEndpoint source, string destination), GraphNodeRecord>();
        foreach ((GraphEdgeRecord edge, _, _, string sourceOwner, string destinationOwner) in crossings)
        {
            if (!stageById.TryGetValue(sourceOwner, out ShaderStage sourceStage))
                throw new InvalidOperationException(
                    $"Connection '{edge.id.value}' refers to unavailable source stage '{sourceOwner}'.");
            if (!stageById.TryGetValue(destinationOwner, out ShaderStage destinationStage))
                throw new InvalidOperationException(
                    $"Connection '{edge.id.value}' refers to unavailable destination stage '{destinationOwner}'.");
            if (sourceStage != ShaderStage.Vertex || destinationStage != ShaderStage.Fragment)
                throw new InvalidOperationException(
                    $"Automatic stage transfer supports Vertex-to-Fragment values only; '{edge.id.value}' connects {sourceStage} to {destinationStage}.");
            if (!ports.TryGetValue(edge.output, out ShaderNodePort? sourcePort)
                || sourcePort.direction != GraphPortDirection.Output)
                throw new InvalidOperationException($"Connection '{edge.id.value}' has no typed source output.");
            if (sourcePort.type.storage is not null
                || sourcePort.type.id.StartsWith("sampled-texture", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Connection '{edge.id.value}' tries to transfer GPU resource type '{sourcePort.type.id}' between stages.");

            if (!sourceBridges.TryGetValue(edge.output, out (string portId, int location) sourceBridge))
            {
                string token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    sourceOwner + "\n" + edge.output.nodeId.value + "\n" + edge.output.portId.value)))[..16];
                string edgeId = "__stage-bridge-" + token + "/write";
                if (graph.edges.Any(value => value.id.value == edgeId))
                    throw new InvalidOperationException($"Generated stage bridge identity '{edgeId}' collides with an authored connection.");
                string portId = "bridge_" + token;
                const string semantic = "texcoord";
                HashSet<int> occupied = occupiedLocations[sourceOwner];
                int location = Enumerable.Range(0, 16).FirstOrDefault(value => !occupied.Contains(value), -1);
                if (location < 0)
                    throw new InvalidOperationException(
                        "Automatic Vertex-to-Fragment transfer exhausted the portable texcoord varying locations 0 through 15.");
                occupied.Add(location);

                GraphNodeRecord sourceOutput = outputById[sourceOwner];
                ShaderGraphStageSettings outputSettings = ShaderGraphDocument.Read(sourceOutput,
                    ShaderGraphDocument.settingsKey, new ShaderGraphStageSettings(), serialization, context);
                outputSettings.outputs =
                [
                    .. outputSettings.outputs,
                    new ShaderGraphOutput
                    {
                        id = portId,
                        kind = ShaderIrOutputKind.Varying,
                        semantic = semantic,
                        location = location
                    }
                ];
                sourceOutput.SetValue(ShaderGraphDocument.settingsKey,
                    ShaderGraphDocument.Encode(outputSettings, serialization, context));
                graph.AddEdge(new(
                    new(edgeId),
                    edge.output,
                    new(sourceOutput.id, new(portId))));
                sourceBridge = (portId, location);
                sourceBridges.Add(edge.output, sourceBridge);
            }

            var key = (edge.output, destinationOwner);
            if (!destinationBridges.TryGetValue(key, out GraphNodeRecord? inputNode))
            {
                string token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    sourceOwner + "\n" + edge.output.nodeId.value + "\n" + edge.output.portId.value + "\n" + destinationOwner)))[..16];
                string id = "__stage-bridge-" + token;
                if (graph.FindNode(new(id)) is not null)
                    throw new InvalidOperationException($"Generated stage bridge identity '{id}' collides with an authored node.");
                inputNode = new(new(id), "inno.shader.stage-input");
                inputNode.SetValue(ShaderGraphDocument.stageKey,
                    ShaderGraphDocument.Encode(destinationOwner, serialization, context));
                inputNode.SetValue(ShaderGraphDocument.settingsKey,
                    ShaderGraphDocument.Encode(new ShaderGraphInputSettings
                    {
                        id = sourceBridge.portId,
                        type = ShaderGraphType.Capture(sourcePort.type),
                        kind = ShaderIrInputKind.Varying,
                        semantic = "texcoord",
                        location = sourceBridge.location
                    }, serialization, context));
                graph.AddNode(inputNode);
                destinationBridges.Add(key, inputNode);
            }

            graph.RemoveEdge(edge.id);
            graph.AddEdge(new(
                new(inputNode.id.value + "/read/" + edge.id.value),
                new(inputNode.id, new("value")),
                edge.input));
        }
    }

    private static void ValidateVaryings(ShaderIrStage vertex, ShaderIrStage fragment)
    {
        foreach (ShaderIrStageInput input in fragment.inputs.Where(static input => input.kind == ShaderIrInputKind.Varying))
        {
            ShaderIrStageOutput? output = vertex.outputs.SingleOrDefault(output => output.kind == ShaderIrOutputKind.Varying
                && output.semantic == input.semantic && output.location == input.location);
            if (output is null || !vertex.body.outputs[output.id].type.IsEquivalentTo(input.type))
                throw new InvalidOperationException($"Fragment varying '{input.semantic}' at {input.location} has no matching vertex output type.");
        }
    }
}
