namespace Inno.Scripting.Compiler;

/// <summary>
/// Identifies the severity of a script compilation diagnostic.
/// </summary>
public enum ScriptDiagnosticSeverity
{
    /// <summary>
    /// An informational compiler message.
    /// </summary>
    Info,
    /// <summary>
    /// A compiler warning.
    /// </summary>
    Warning,
    /// <summary>
    /// A compiler error.
    /// </summary>
    Error
}

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
public sealed record ScriptDiagnostic(
    string id,
    ScriptDiagnosticSeverity severity,
    string message,
    string? filePath,
    int line,
    int column);
