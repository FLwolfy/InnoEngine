using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Diagnostics;

namespace Inno.Rendering.Shaders;

/// <summary>Freezes the graph-lowered stages of one material-selectable GPU pass.</summary>
public sealed class ShaderGraphPass
{
    /// <summary>Captures a pass's ordered typed stages.</summary>
    /// <param name="name">Exact pass identity in the program contract.</param>
    /// <param name="stages">Complete raster or compute stage set.</param>
    public ShaderGraphPass(string name, IEnumerable<ShaderIrStage> stages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stages);
        this.name = name;
        this.stages = Array.AsReadOnly(stages.ToArray());
    }
    /// <summary>Gets the stable pass identity.</summary>
    public string name { get; }
    /// <summary>Gets immutable graph-lowered stages.</summary>
    public IReadOnlyList<ShaderIrStage> stages { get; }
}

/// <summary>Contains the result of lowering an entire shader graph, before target-native compilation.</summary>
public sealed class ShaderGraphProgramResult
{
    internal ShaderGraphProgramResult(IEnumerable<ShaderGraphPass> passes, IEnumerable<ShaderGraphDiagnostic> diagnostics)
    {
        this.passes = Array.AsReadOnly(passes.ToArray());
        this.diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
    /// <summary>Gets the complete stage set; empty if any stage failed.</summary>
    public IReadOnlyList<ShaderGraphPass> passes { get; }
    /// <summary>Gets stable graph/node diagnostics without provider references.</summary>
    public IReadOnlyList<ShaderGraphDiagnostic> diagnostics { get; }
    /// <summary>Gets whether every declared pass was lowered without errors.</summary>
    public bool succeeded => passes.Count != 0 && diagnostics.All(static diagnostic => diagnostic.severity != DiagnosticSeverity.Error);
}
