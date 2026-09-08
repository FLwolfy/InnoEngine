using Inno.Core.Diagnostics;
namespace Inno.Scripting.Compiler;

/// <summary>
/// Represents one source-oriented script compilation diagnostic.
/// </summary>
/// <param name="id">
/// The stable compiler, analyzer, or reload diagnostic identifier.
/// </param>
/// <param name="severity">
/// The presentation severity of the diagnostic.
/// </param>
/// <param name="message">
/// The human-readable diagnostic message.
/// </param>
/// <param name="filePath">
/// The source path associated with the diagnostic, or <see langword="null"/> for a project-level diagnostic.
/// </param>
/// <param name="line">
/// The one-based source line, or zero when no source location is available.
/// </param>
/// <param name="column">
/// The one-based source column, or zero when no source location is available.
/// </param>
/// <returns>
/// The value produced by this implementation of the contract.
/// </returns>
public sealed record ScriptDiagnostic(
    string id,
    DiagnosticSeverity severity,
    string message,
    string? filePath,
    int line,
    int column);
