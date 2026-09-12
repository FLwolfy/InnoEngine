using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Diagnostics;

namespace Inno.Rendering.Shaders;

/// <summary>Identifies an original source position, not a generated temporary compiler filename.</summary>
/// <param name="assetPath">Source path in the current asset mount.</param>
/// <param name="line">One-based source line.</param>
/// <param name="column">One-based source column.</param>
public readonly record struct ShaderSourcePosition(string assetPath, int line, int column);

/// <summary>Reports a frontend problem without retaining a language-specific exception or parser.</summary>
/// <param name="code">Stable diagnostic code.</param>
/// <param name="severity">Diagnostic severity.</param>
/// <param name="message">Actionable diagnostic description.</param>
/// <param name="location">Original source position.</param>
public sealed record ShaderSourceDiagnostic(string code, DiagnosticSeverity severity, string message, ShaderSourcePosition location);

/// <summary>Contains immutable source text supplied by the asset candidate's controlled source resolver.</summary>
/// <param name="assetPath">Resolved asset mount path.</param>
/// <param name="text">UTF-8 decoded immutable source text.</param>
public sealed record ShaderSourceFile(string assetPath, string text);

/// <summary>Reads dependencies only from the current source candidate and records their import dependencies.</summary>
public interface IShaderSourceResolver
{
    /// <summary>Resolves and reads an include without bypassing asset mount permissions.</summary>
    /// <param name="includingFile">Resolved path of the including source.</param>
    /// <param name="include">Source-local include spelling.</param>
    /// <returns>A resolved immutable source snapshot.</returns>
    ShaderSourceFile ReadInclude(string includingFile, string include);
}

/// <summary>Captures all inputs required to analyze one source implementation.</summary>
public sealed class ShaderSourceRequest
{
    /// <summary>Creates an analysis request for an explicitly selected function.</summary>
    /// <param name="source">Root source snapshot.</param>
    /// <param name="entryPoint">The one public function selected by source import settings.</param>
    /// <param name="resolver">Candidate-scoped include resolver.</param>
    /// <param name="defines">Target and variant preprocessing inputs, or null for no defines.</param>
    public ShaderSourceRequest(ShaderSourceFile source, string entryPoint, IShaderSourceResolver resolver,
        IReadOnlyDictionary<string, string>? defines = null)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        ArgumentException.ThrowIfNullOrWhiteSpace(source.assetPath);
        ArgumentNullException.ThrowIfNull(source.text);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);
        this.entryPoint = entryPoint;
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        this.defines = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            defines is null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(defines, StringComparer.Ordinal));
    }
    /// <summary>Gets the root source snapshot.</summary>
    public ShaderSourceFile source { get; }
    /// <summary>Gets the explicitly selected export function name.</summary>
    public string entryPoint { get; }
    /// <summary>Gets the candidate-scoped dependency resolver.</summary>
    public IShaderSourceResolver resolver { get; }
    /// <summary>Gets immutable preprocessing inputs included in analysis cache identity.</summary>
    public IReadOnlyDictionary<string, string> defines { get; }
}

/// <summary>Returns a recoverable source analysis result and complete original-source dependencies.</summary>
public sealed class ShaderSourceAnalysis
{
    /// <summary>Captures a completed analysis without retaining compiler objects.</summary>
    /// <param name="function">Parsed interface, or null when parsing failed.</param>
    /// <param name="dependencies">Resolved included sources, excluding the root.</param>
    /// <param name="diagnostics">All problems discovered by the frontend.</param>
    public ShaderSourceAnalysis(ShaderSourceFunction? function, IEnumerable<string> dependencies,
        IEnumerable<ShaderSourceDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.function = function;
        this.dependencies = Array.AsReadOnly(dependencies.Distinct(StringComparer.Ordinal).ToArray());
        this.diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
    /// <summary>Gets the parsed interface, or null after an analysis failure.</summary>
    public ShaderSourceFunction? function { get; }
    /// <summary>Gets original source dependencies for invalidation.</summary>
    public IReadOnlyList<string> dependencies { get; }
    /// <summary>Gets immutable, provider-neutral diagnostics.</summary>
    public IReadOnlyList<ShaderSourceDiagnostic> diagnostics { get; }
    /// <summary>Gets whether a usable interface was found without errors.</summary>
    public bool succeeded => function is not null && !diagnostics.Any(static item => item.severity == DiagnosticSeverity.Error);
}

/// <summary>Parses one source language into canonical function interfaces; it does not create GPU objects.</summary>
public interface IShaderSourceFrontend
{
    /// <summary>Gets the open source language identity, distinct from a rendering backend or GPU API.</summary>
    string languageId { get; }
    /// <summary>Analyzes an immutable source candidate with explicit preprocessing inputs.</summary>
    /// <param name="request">Source, selected function, dependencies, and target inputs.</param>
    /// <returns>A successful interface or a recoverable set of original-source diagnostics.</returns>
    ShaderSourceAnalysis Analyze(ShaderSourceRequest request);
}
