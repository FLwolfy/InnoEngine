using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Inno.Core.Diagnostics;
using Inno.Core.Graphs;

namespace Inno.Rendering.Shaders;

/// <summary>Freezes one target-selected graph region and its externally resolved, neutral compilation inputs.</summary>
public sealed class ShaderGraphLoweringRequest
{
    internal GraphDocument graph { get; }

    /// <summary>Captures a region without retaining an asset object, source resolver, node provider or UI selection.</summary>
    /// <param name="graph">Nodes and edges in this stage region; canvas positions are not compilation inputs.</param>
    /// <param name="outputs">Named region results mapped to stable output endpoints.</param>
    /// <param name="implementationId">Exact source implementation key selected by the target.</param>
    /// <param name="sourceModules">Already analyzed source modules keyed by their source node identity.</param>
    /// <param name="stageInputs">Target-assigned stage inputs keyed by their input node identity.</param>
    public ShaderGraphLoweringRequest(GraphDocument graph, IReadOnlyDictionary<string, GraphEndpoint> outputs,
        string implementationId, IReadOnlyDictionary<GraphNodeId, ShaderSourceModuleAnalysis>? sourceModules = null,
        IReadOnlyDictionary<GraphNodeId, ShaderIrStageInput>? stageInputs = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationId);
        foreach (string name in outputs.Keys) ArgumentException.ThrowIfNullOrWhiteSpace(name);
        this.graph = graph.Clone();
        this.outputs = new ReadOnlyDictionary<string, GraphEndpoint>(new Dictionary<string, GraphEndpoint>(outputs, StringComparer.Ordinal));
        this.implementationId = implementationId;
        this.sourceModules = new ReadOnlyDictionary<GraphNodeId, ShaderSourceModuleAnalysis>(new Dictionary<GraphNodeId, ShaderSourceModuleAnalysis>(sourceModules ?? new Dictionary<GraphNodeId, ShaderSourceModuleAnalysis>()));
        this.stageInputs = new ReadOnlyDictionary<GraphNodeId, ShaderIrStageInput>(new Dictionary<GraphNodeId, ShaderIrStageInput>(stageInputs ?? new Dictionary<GraphNodeId, ShaderIrStageInput>()));
    }

    /// <summary>Gets named region outputs by stable graph endpoint.</summary>
    public IReadOnlyDictionary<string, GraphEndpoint> outputs { get; }
    /// <summary>Gets the exact implementation key selected by the target.</summary>
    public string implementationId { get; }
    /// <summary>Gets frozen source modules, including unavailable/failed modules for accurate diagnostics.</summary>
    public IReadOnlyDictionary<GraphNodeId, ShaderSourceModuleAnalysis> sourceModules { get; }
    /// <summary>Gets target-assigned input descriptors; they contain no native expressions.</summary>
    public IReadOnlyDictionary<GraphNodeId, ShaderIrStageInput> stageInputs { get; }
}

/// <summary>Identifies a graph compilation problem without retaining a node or compiler instance.</summary>
/// <param name="code">Stable diagnostic identity.</param>
/// <param name="severity">Diagnostic severity.</param>
/// <param name="message">Human-readable explanation.</param>
/// <param name="nodeId">Affected node identity when known.</param>
/// <param name="portId">Affected semantic port when known.</param>
public sealed record ShaderGraphDiagnostic(string code, DiagnosticSeverity severity, string message,
    GraphNodeId? nodeId = null, string? portId = null);

/// <summary>Contains a detached typed region only after all graph, port and node lowering validation succeeds.</summary>
public sealed class ShaderGraphLoweringResult
{
    internal ShaderGraphLoweringResult(ShaderIrBlock? block, IEnumerable<ShaderGraphDiagnostic> diagnostics)
    {
        this.block = block;
        this.diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
    /// <summary>Gets the immutable typed region, or null on failure.</summary>
    public ShaderIrBlock? block { get; }
    /// <summary>Gets located graph diagnostics, including missing definitions and stale source ports.</summary>
    public IReadOnlyList<ShaderGraphDiagnostic> diagnostics { get; }
    /// <summary>Gets whether lowering produced a complete region without errors; native compilation is a separate gate.</summary>
    public bool succeeded => block is not null && diagnostics.All(static value => value.severity != DiagnosticSeverity.Error);
}
