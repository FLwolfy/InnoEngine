namespace Inno.Editor.Scripting;

/// <summary>
/// Identifies the severity of a script compilation diagnostic.
/// </summary>
public enum ScriptDiagnosticSeverity
{
    /// <summary>An informational compiler message.</summary>
    Info,
    /// <summary>A compiler warning.</summary>
    Warning,
    /// <summary>A compiler error.</summary>
    Error
}

/// <summary>
/// Represents one source-oriented script compilation diagnostic.
/// </summary>
public sealed record ScriptDiagnostic(
    string id,
    ScriptDiagnosticSeverity severity,
    string message,
    string? filePath,
    int line,
    int column);
