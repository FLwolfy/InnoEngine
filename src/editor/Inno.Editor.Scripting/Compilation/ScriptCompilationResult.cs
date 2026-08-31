using System;
using System.Collections.Generic;

using Inno.Core.Assemblies;

namespace Inno.Editor.Scripting;

/// <summary>
/// Reports the outputs and diagnostics of one complete game/editor script compilation.
/// </summary>
public sealed class ScriptCompilationResult
{
    internal ScriptCompilationResult(
        bool success,
        IReadOnlyList<ScriptDiagnostic> diagnostics,
        string? outputDirectory,
        IReadOnlyList<AssemblyLoadRequest>? reloadRequests,
        IReadOnlyList<string>? compiledAssemblies = null,
        IReadOnlyList<string>? reusedAssemblies = null)
    {
        this.success = success;
        this.diagnostics = diagnostics;
        this.outputDirectory = outputDirectory;
        this.reloadRequests = reloadRequests ?? [];
        this.compiledAssemblies = compiledAssemblies ?? [];
        this.reusedAssemblies = reusedAssemblies ?? [];
    }

    /// <summary>Gets whether every discovered script assembly compiled or reused successfully.</summary>
    public bool success { get; }

    /// <summary>Gets all diagnostics produced by the compilation.</summary>
    public IReadOnlyList<ScriptDiagnostic> diagnostics { get; }

    /// <summary>Gets the generation output directory when one was created.</summary>
    public string? outputDirectory { get; }

    internal IReadOnlyList<AssemblyLoadRequest> reloadRequests { get; }

    internal IReadOnlyList<string> compiledAssemblies { get; }

    internal IReadOnlyList<string> reusedAssemblies { get; }
}
