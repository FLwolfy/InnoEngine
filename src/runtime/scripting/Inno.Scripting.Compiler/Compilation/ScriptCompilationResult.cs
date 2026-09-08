using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Extensibility.Modules;

namespace Inno.Scripting.Compiler;

/// <summary>
/// Reports the outputs and diagnostics of one authoring-generation or runtime-deployment script compilation.
/// </summary>
public sealed class ScriptCompilationResult
{
    internal ScriptCompilationResult(
        bool success,
        IReadOnlyList<ScriptDiagnostic> diagnostics,
        string? outputDirectory,
        IReadOnlyList<AssemblyLoadRequest>? reloadRequests,
        IReadOnlyList<string>? compiledAssemblies = null,
        IReadOnlyList<string>? reusedAssemblies = null,
        IReadOnlyList<ScriptCompilationStageTiming>? stageTimings = null)
    {
        this.success = success;
        this.diagnostics = diagnostics;
        this.outputDirectory = outputDirectory;
        this.reloadRequests = reloadRequests ?? [];
        runtimeAssemblyPaths = this.reloadRequests
            .SelectMany(static request =>
                new[] { request.mainAssemblyPath }.Concat(request.preloadAssemblyPaths)
                    .Where(path => request.assemblyScopes.TryGetValue(
                            Path.GetFileNameWithoutExtension(path),
                            out AssemblyScope scope)
                        ? scope == AssemblyScope.Runtime
                        : request.scope == AssemblyScope.Runtime))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        this.compiledAssemblies = compiledAssemblies ?? [];
        this.reusedAssemblies = reusedAssemblies ?? [];
        this.stageTimings = stageTimings ?? [];
    }

    /// <summary>
    /// Gets whether every discovered script assembly compiled or reused successfully.
    /// </summary>
    public bool success { get; }

    /// <summary>
    /// Gets all diagnostics produced by the compilation.
    /// </summary>
    public IReadOnlyList<ScriptDiagnostic> diagnostics { get; }

    /// <summary>
    /// Gets the generation output directory when one was created.
    /// </summary>
    public string? outputDirectory { get; }

    /// <summary>
    /// Gets completed compiler stage timings in execution order.
    /// </summary>
    public IReadOnlyList<ScriptCompilationStageTiming> stageTimings { get; }

    /// <summary>
    /// Gets exact runtime-scope managed assemblies suitable for a deployed Player.
    /// </summary>
    public IReadOnlyList<string> runtimeAssemblyPaths { get; }

    /// <summary>
    /// Gets the validated module activation requests associated with this artifact generation.
    /// </summary>
    public IReadOnlyList<AssemblyLoadRequest> activationRequests => reloadRequests;

    /// <summary>
    /// Gets assembly names compiled during this request instead of reused from the artifact cache.
    /// </summary>
    public IReadOnlyList<string> compiledAssemblyNames => compiledAssemblies;

    /// <summary>
    /// Gets assembly names reused from the deterministic artifact cache.
    /// </summary>
    public IReadOnlyList<string> reusedAssemblyNames => reusedAssemblies;

    /// <summary>
    /// Creates a failed result for an exception intercepted at an orchestration boundary.
    /// </summary>
    /// <param name="diagnostic">
    /// The diagnostic describing why no candidate generation was produced.
    /// </param>
    /// <returns>
    /// A failed immutable result with no output directory or activation requests.
    /// </returns>
    public static ScriptCompilationResult Failure(ScriptDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return new ScriptCompilationResult(
            success: false,
            [diagnostic],
            outputDirectory: null,
            reloadRequests: null);
    }

    internal IReadOnlyList<AssemblyLoadRequest> reloadRequests { get; }

    internal IReadOnlyList<string> compiledAssemblies { get; }

    internal IReadOnlyList<string> reusedAssemblies { get; }
}
