using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Core.Storage;

namespace Inno.Rendering.ShaderGraph;

/// <summary>Contains either a shared Shader IR module or graph/node-mapped diagnostics.</summary>
public sealed class ShaderGraphCompileResult
{
    /// <summary>Creates a shader graph compilation result.</summary>
    /// <param name="module">Generated shared Shader IR, or <see langword="null"/> after failure.</param>
    /// <param name="diagnostics">Structured graph and source diagnostics.</param>
    public ShaderGraphCompileResult(ShaderIRModule? module, IReadOnlyList<ShaderDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.module = module;
        this.diagnostics = diagnostics;
    }

    /// <summary>Gets generated shared Shader IR, or <see langword="null"/> after failure.</summary>
    public ShaderIRModule? module { get; }

    /// <summary>Gets graph and source diagnostics.</summary>
    public IReadOnlyList<ShaderDiagnostic> diagnostics { get; }

    /// <summary>Gets whether a complete valid IR module was generated.</summary>
    public bool succeeded => module is not null
        && diagnostics.All(static value => value.severity != ShaderDiagnosticSeverity.Error);
}

/// <summary>Compiles Plugin-defined shader nodes into the same IR used by handwritten shaders.</summary>
public static class ShaderGraphCompiler
{
    /// <summary>Compiles one imported shader graph asset into shared Shader IR.</summary>
    /// <param name="asset">Imported graph asset.</param>
    /// <param name="registry">Active Plugin node generation.</param>
    /// <returns>Generated IR or node-mapped diagnostics.</returns>
    public static ShaderGraphCompileResult Compile(ShaderGraphAsset asset, ShaderNodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(registry);
        if (asset.document is null)
        {
            return Failure("SHADER_GRAPH_DOCUMENT_MISSING", $"Shader graph '{asset.name}' has no document.");
        }
        ShaderGraphCompileResult result = Compile(
            string.IsNullOrWhiteSpace(asset.assetPath.ToString()) ? asset.name : asset.assetPath.ToString(),
            asset.name,
            asset.document,
            registry);
        if (result.succeeded && result.module is not null)
            asset.CommitDefinition(result.module.definition);
        return result;
    }

    /// <summary>Compiles one neutral graph through its Plugin-provided program output node.</summary>
    /// <param name="assetPath">Canonical asset path used by diagnostics.</param>
    /// <param name="shaderName">Artist-facing generated shader name.</param>
    /// <param name="document">Neutral graph document.</param>
    /// <param name="registry">Active Plugin node generation.</param>
    /// <returns>Generated shared IR or structured diagnostics.</returns>
    public static ShaderGraphCompileResult Compile(
        string assetPath,
        string shaderName,
        GraphDocument document,
        ShaderNodeRegistry registry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(registry);
        List<ShaderDiagnostic> diagnostics = ValidateGraph(assetPath, document, registry);
        if (HasErrors(diagnostics))
            return new ShaderGraphCompileResult(null, diagnostics);

        (GraphNodeRecord node, ShaderGraphProgramNodeDefinition definition)[] outputs = document.nodes
            .Select(node => registry.TryResolveShader(node.definitionId, out ShaderNodeDefinition? definition)
                && definition is ShaderGraphProgramNodeDefinition program
                    ? (node, program)
                    : default)
            .Where(static pair => pair.node is not null && pair.program is not null)
            .Select(static pair => (pair.node!, pair.program!))
            .ToArray();
        if (outputs.Length != 1)
        {
            diagnostics.Add(new ShaderDiagnostic(
                "SHADER_GRAPH_PROGRAM_OUTPUT_COUNT",
                ShaderDiagnosticSeverity.Error,
                $"A shader graph requires exactly one Plugin-defined program output node; found {outputs.Length}."));
            return new ShaderGraphCompileResult(null, diagnostics);
        }

        (GraphNodeRecord outputNode, ShaderGraphProgramNodeDefinition outputDefinition) = outputs[0];
        try
        {
            var emissions = new Dictionary<ShaderStage, ShaderGraphEmission>();
            var context = new ShaderGraphProgramContext(
                assetPath,
                shaderName,
                document,
                outputNode,
                stage =>
                {
                    if (!IsSingleStage(stage))
                        throw new ArgumentOutOfRangeException(nameof(stage), "One concrete shader stage is required.");
                    if (!emissions.TryGetValue(stage, out ShaderGraphEmission? emission))
                    {
                        emission = EmitGraph(assetPath, stage, document, registry, outputNode, diagnostics);
                        emissions.Add(stage, emission);
                    }
                    return emission;
                });
            ShaderIRModule module = outputDefinition.BuildProgram(context);
            ShaderIRValidationResult validation = ShaderIRValidator.Validate(module);
            diagnostics.AddRange(validation.diagnostics);
            return HasErrors(diagnostics)
                ? new ShaderGraphCompileResult(null, diagnostics)
                : new ShaderGraphCompileResult(module, diagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.Add(new ShaderDiagnostic(
                "SHADER_GRAPH_PROGRAM_EMIT_FAILED",
                ShaderDiagnosticSeverity.Error,
                $"Program output '{outputDefinition.displayName}' failed: {exception.Message}",
                Location(assetPath, ShaderStage.None, outputNode.id)));
            return new ShaderGraphCompileResult(null, diagnostics);
        }
    }

    private static List<ShaderDiagnostic> ValidateGraph(
        string assetPath,
        GraphDocument document,
        ShaderNodeRegistry registry)
    {
        GraphValidationResult validation = GraphValidator.Validate(
            document,
            registry,
            new ShaderGraphTypeConversion());
        return validation.diagnostics.Select(diagnostic => new ShaderDiagnostic(
            $"SHADER_{diagnostic.code}",
            diagnostic.severity == GraphDiagnosticSeverity.Error
                || diagnostic.code == "GRAPH_MISSING_NODE"
                    ? ShaderDiagnosticSeverity.Error
                    : diagnostic.severity == GraphDiagnosticSeverity.Warning
                        ? ShaderDiagnosticSeverity.Warning
                        : ShaderDiagnosticSeverity.Info,
            diagnostic.message,
            diagnostic.nodeId is GraphNodeId nodeId
                ? Location(assetPath, ShaderStage.None, nodeId)
                : null)).ToList();
    }

    private static ShaderGraphEmission EmitGraph(
        string assetPath,
        ShaderStage stage,
        GraphDocument document,
        ShaderNodeRegistry registry,
        GraphNodeRecord output,
        List<ShaderDiagnostic> diagnostics)
    {
        IReadOnlyList<GraphNodeRecord> order = TopologicalOrder(document);
        HashSet<GraphNodeId> activeNodes = CollectAncestors(document, output.id);
        var values = new Dictionary<GraphEndpoint, ShaderValue>();
        var properties = new Dictionary<string, ShaderPropertyDefinition>(StringComparer.Ordinal);
        var semantics = new Dictionary<string, ShaderValue>(StringComparer.Ordinal);
        var statements = new List<string>();
        var ports = new Dictionary<GraphNodeId, IReadOnlyDictionary<GraphPortId, GraphPortDefinition>>();
        foreach (GraphNodeRecord node in document.nodes)
        {
            if (registry.TryResolveShader(node.definitionId, out ShaderNodeDefinition? definition)
                && definition is not null)
            {
                ports[node.id] = definition.GetPorts(node).ToDictionary(static value => value.id);
            }
        }

        foreach (GraphNodeRecord node in order.Where(node => activeNodes.Contains(node.id)))
        {
            if (!registry.TryResolveShader(node.definitionId, out ShaderNodeDefinition? definition)
                || definition is null)
            {
                continue;
            }
            if ((definition.supportedStages & stage) == 0)
            {
                diagnostics.Add(new ShaderDiagnostic(
                    "SHADER_GRAPH_STAGE_ILLEGAL",
                    ShaderDiagnosticSeverity.Error,
                    $"Node '{definition.displayName}' cannot execute in the {stage} stage.",
                    Location(assetPath, stage, node.id)));
                continue;
            }
            var inputs = new Dictionary<GraphPortId, ShaderValue>();
            foreach (GraphEdgeRecord edge in document.edges.Where(candidate => candidate.input.nodeId == node.id))
            {
                if (!values.TryGetValue(edge.output, out ShaderValue source))
                    throw new InvalidOperationException($"Upstream value '{edge.output}' was not emitted.");
                GraphPortDefinition inputPort = ports[node.id][edge.input.portId];
                inputs[edge.input.portId] = Convert(source, ShaderGraphValueTypes.Parse(inputPort.valueTypeId));
            }
            var emitContext = new ShaderNodeEmitContext(
                node,
                stage,
                inputs,
                (port, value) => values[new GraphEndpoint(node.id, port)] = value,
                statements.Add,
                property => DeclareProperty(properties, property),
                (semantic, value) =>
                {
                    if (node.id == output.id)
                        semantics[semantic] = value;
                });
            try
            {
                definition.Emit(emitContext);
            }
            catch (Exception exception)
            {
                diagnostics.Add(new ShaderDiagnostic(
                    "SHADER_GRAPH_NODE_EMIT_FAILED",
                    ShaderDiagnosticSeverity.Error,
                    $"Node '{definition.displayName}' failed: {exception.Message}",
                    Location(assetPath, stage, node.id)));
            }
        }
        return new ShaderGraphEmission(
            stage,
            properties.Values.OrderBy(static value => value.id.value, StringComparer.Ordinal).ToArray(),
            semantics,
            statements,
            output.id);
    }

    private static ShaderValue Convert(ShaderValue value, ShaderValueType destination)
    {
        if (value.type == destination)
            return value;
        string expression = (value.type, destination) switch
        {
            (ShaderValueType.Float, ShaderValueType.Float2) => $"vec2({value.expression})",
            (ShaderValueType.Float, ShaderValueType.Float3) => $"vec3({value.expression})",
            (ShaderValueType.Float, ShaderValueType.Float4 or ShaderValueType.Color) => $"vec4({value.expression})",
            (ShaderValueType.Float3, ShaderValueType.Float4 or ShaderValueType.Color) => $"vec4({value.expression}, 1.0)",
            (ShaderValueType.Float4, ShaderValueType.Color) => value.expression,
            (ShaderValueType.Color, ShaderValueType.Float4) => value.expression,
            _ => throw new InvalidOperationException($"Cannot convert {value.type} to {destination}.")
        };
        return new ShaderValue(destination, expression, value.sourceNodeId);
    }

    private static void DeclareProperty(
        IDictionary<string, ShaderPropertyDefinition> properties,
        ShaderPropertyDefinition property)
    {
        if (properties.TryGetValue(property.id.value, out ShaderPropertyDefinition existing)
            && (!existing.Equals(property)))
        {
            throw new InvalidOperationException($"Shader property '{property.id}' has conflicting declarations.");
        }
        properties[property.id.value] = property;
    }

    private static IReadOnlyList<GraphNodeRecord> TopologicalOrder(GraphDocument document)
    {
        var byId = document.nodes.ToDictionary(static node => node.id);
        IComparer<GraphNodeId> ordering = Comparer<GraphNodeId>.Create(static (left, right) =>
            StringComparer.Ordinal.Compare(left.value, right.value));
        var graph = new DependencyGraph<GraphNodeId>(orderingComparer: ordering);
        foreach (GraphNodeRecord node in document.nodes)
            graph.AddNode(node.id);
        foreach (GraphEdgeRecord edge in document.edges)
        {
            if (!byId.ContainsKey(edge.input.nodeId) || !byId.ContainsKey(edge.output.nodeId))
                throw new InvalidOperationException("Shader graph contains an edge with a missing endpoint.");
            graph.AddDependency(edge.input.nodeId, edge.output.nodeId);
        }
        try
        {
            return graph.TopologicalSort().Select(id => byId[id]).ToArray();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"Shader graph contains a cycle. {exception.Message}", exception);
        }
    }

    private static HashSet<GraphNodeId> CollectAncestors(GraphDocument document, GraphNodeId root)
    {
        var result = new HashSet<GraphNodeId>();
        var pending = new Stack<GraphNodeId>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            GraphNodeId node = pending.Pop();
            if (!result.Add(node))
                continue;
            foreach (GraphEdgeRecord edge in document.edges.Where(edge => edge.input.nodeId == node))
                pending.Push(edge.output.nodeId);
        }
        return result;
    }

    private static bool HasErrors(IEnumerable<ShaderDiagnostic> diagnostics)
        => diagnostics.Any(static value => value.severity == ShaderDiagnosticSeverity.Error);

    private static bool IsSingleStage(ShaderStage stage)
        => stage is ShaderStage.Vertex or ShaderStage.Fragment or ShaderStage.Compute;

    private static ShaderSourceLocation Location(
        string assetPath,
        ShaderStage stage,
        GraphNodeId nodeId)
        => new(assetPath, "Graph", stage, nodeId: nodeId.value);

    private static ShaderGraphCompileResult Failure(string code, string message)
        => new(null, [new ShaderDiagnostic(code, ShaderDiagnosticSeverity.Error, message)]);
}
