using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Inno.Core.Diagnostics;

namespace Inno.Rendering.Shaders;

/// <summary>Owns an immutable language registration snapshot for one authoring generation.</summary>
public sealed class ShaderSourceFrontendCatalog
{
    private readonly Dictionary<string, IShaderSourceFrontend> m_frontends = new(StringComparer.Ordinal);

    /// <summary>Captures a validated language provider set without a process-global registry.</summary>
    /// <param name="frontends">Providers owned and retired by the calling generation.</param>
    /// <exception cref="ArgumentException">Language IDs are missing or duplicated.</exception>
    public ShaderSourceFrontendCatalog(IEnumerable<IShaderSourceFrontend> frontends)
    {
        ArgumentNullException.ThrowIfNull(frontends);
        foreach (IShaderSourceFrontend frontend in frontends)
            if (frontend is null || string.IsNullOrWhiteSpace(frontend.languageId) ||
                !m_frontends.TryAdd(frontend.languageId, frontend))
                throw new ArgumentException("Source frontends require unique, assigned language IDs.", nameof(frontends));
        languageIds = new ReadOnlyCollection<string>(new List<string>(m_frontends.Keys));
    }
    /// <summary>Gets registered language identities for source import settings.</summary>
    public IReadOnlyList<string> languageIds { get; }

    internal IEnumerable<IShaderSourceFrontend> providers => m_frontends.Values;

    /// <summary>Analyzes source using an explicitly selected language.</summary>
    /// <param name="languageId">Registered language identity, never inferred from the active graphics API.</param>
    /// <param name="request">Immutable source analysis request.</param>
    /// <returns>The selected frontend's analysis result.</returns>
    /// <exception cref="NotSupportedException">The requested language provider is unavailable.</exception>
    public ShaderSourceAnalysis Analyze(string languageId, ShaderSourceRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageId);
        ArgumentNullException.ThrowIfNull(request);
        return m_frontends.TryGetValue(languageId, out IShaderSourceFrontend? frontend)
            ? frontend.Analyze(request)
            : throw new NotSupportedException($"Shader source language '{languageId}' is unavailable.");
    }

    /// <summary>Analyzes all selected implementations and variants against one immutable provider generation.</summary>
    /// <param name="implementations">Explicit implementation/configuration candidates; no native-code translation is attempted.</param>
    /// <returns>Frozen source inputs and a common interface, or recoverable missing/interface diagnostics.</returns>
    /// <exception cref="ArgumentException">The candidate list is empty or contains null or duplicate implementation IDs.</exception>
    public ShaderSourceModuleAnalysis AnalyzeModule(IEnumerable<ShaderSourceImplementationRequest> implementations)
    {
        ArgumentNullException.ThrowIfNull(implementations);
        ShaderSourceImplementationRequest[] requests = implementations.ToArray();
        if (requests.Length == 0 || requests.Any(static value => value is null) ||
            requests.Select(static value => value.implementationId).Distinct(StringComparer.Ordinal).Count() != requests.Length)
            throw new ArgumentException("A module requires unique, assigned implementation/configuration IDs.", nameof(implementations));

        var results = new List<ShaderSourceImplementationAnalysis>();
        var diagnostics = new List<ShaderSourceDiagnostic>();
        ShaderSourceFunction? common = null;
        string? commonId = null;
        foreach (ShaderSourceImplementationRequest request in requests.OrderBy(static value => value.implementationId, StringComparer.Ordinal))
        {
            var resolver = new ShaderSourceSnapshotResolver(request.source);
            ShaderSourceAnalysis analysis;
            if (!m_frontends.TryGetValue(request.languageId, out IShaderSourceFrontend? frontend))
                analysis = new(null, [], [new("SHADER_SOURCE_LANGUAGE_MISSING", DiagnosticSeverity.Error,
                    $"Implementation '{request.implementationId}' requires unavailable language '{request.languageId}'.",
                    new(request.source.source.assetPath, 1, 1))]);
            else
            {
                analysis = frontend.Analyze(new(request.source.source, request.source.entryPoint, resolver, request.source.defines))
                    ?? throw new InvalidOperationException($"Language '{request.languageId}' returned no source analysis.");
                var paths = new HashSet<string>(resolver.files.Select(static file => file.assetPath), StringComparer.Ordinal);
                string[] uncaptured = analysis.dependencies.Where(path => !paths.Contains(path)).ToArray();
                if (analysis.succeeded && uncaptured.Length != 0)
                    analysis = new(analysis.function, analysis.dependencies, analysis.diagnostics.Concat(
                        uncaptured.Select(path => new ShaderSourceDiagnostic("SHADER_SOURCE_DEPENDENCY_UNCAPTURED", DiagnosticSeverity.Error,
                            $"Language '{request.languageId}' reported dependency '{path}' without reading it through the source resolver.",
                            new(request.source.source.assetPath, 1, 1)))));
                analysis = new(analysis.function,
                    analysis.dependencies.Concat(paths.Where(path => path != request.source.source.assetPath)), analysis.diagnostics);
            }
            results.Add(new(request, analysis, resolver.files, resolver.includes));
            diagnostics.AddRange(analysis.diagnostics);
            if (!analysis.succeeded) continue;
            if (common is null)
            {
                common = analysis.function;
                commonId = request.implementationId;
            }
            else if (!common.HasSameInterface(analysis.function))
                diagnostics.Add(new("SHADER_SOURCE_IMPLEMENTATION_INTERFACE", DiagnosticSeverity.Error,
                    $"Implementation '{request.implementationId}' does not match '{commonId}': public parameter names, directions, order and complete value types must agree.",
                    analysis.function!.location));
        }
        return new(results, diagnostics);
    }
}
