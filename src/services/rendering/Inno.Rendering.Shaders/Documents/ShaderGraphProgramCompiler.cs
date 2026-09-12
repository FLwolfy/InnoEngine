using System;
using System.Collections.Generic;
using System.Linq;
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
            var definitions = definition.passes.ToDictionary(static pass => pass.name, StringComparer.Ordinal);
            if (definitions.Count == 0) throw new InvalidOperationException("A shader graph requires at least one pass.");
            var stages = new Dictionary<(string pass, ShaderStage stage), ShaderIrStage>();
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
                if (!definitions.ContainsKey(settings.pass)) throw new InvalidOperationException($"Stage refers to unknown pass '{settings.pass}'.");
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
                        throw new InvalidOperationException("A connection crosses stage boundaries; use an explicit varying interface.");
                    if (edge.output.nodeId == output.id) throw new InvalidOperationException("A GPU stage output node cannot produce graph values.");
                }
                string[] expected = settings.outputs.Select(static value => value.id).ToArray();
                if (expected.Distinct(StringComparer.Ordinal).Count() != expected.Length || !expected.ToHashSet(StringComparer.Ordinal).SetEquals(endpoints.Keys))
                    throw new InvalidOperationException("Every stage output must have exactly one connection with its original port identity.");
                var inputs = region.nodes.Where(static node => node.definitionId == "inno.shader.stage-input")
                    .ToDictionary(static node => node.id, node =>
                        (ShaderGraphDocument.Read<ShaderGraphInputSettings?>(node, ShaderGraphDocument.settingsKey, null, serialization, context)
                         ?? throw new InvalidOperationException("A stage input requires its interface settings.")).CreateBinding());
                ShaderGraphLoweringResult lowered = m_nodes.Lower(new(region, endpoints, implementationId,
                    sources.Where(pair => members.Contains(pair.Key)).ToDictionary(), inputs), serialization, context, cancellationToken);
                diagnostics.AddRange(lowered.diagnostics);
                if (!lowered.succeeded) continue;
                var stage = new ShaderIrStage(settings.stage, lowered.block!, inputs.Values,
                    settings.outputs.Select(static value => new ShaderIrStageOutput(value.id, value.kind, value.semantic ?? "", value.location)),
                    settings.threadsX, settings.threadsY, settings.threadsZ);
                if (!stages.TryAdd((settings.pass, settings.stage), stage)) throw new InvalidOperationException($"Pass '{settings.pass}' repeats {settings.stage}.");
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
                var selected = stages.Where(pair => pair.Key.pass == pass.name).ToDictionary(static pair => pair.Key.stage, static pair => pair.Value);
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
