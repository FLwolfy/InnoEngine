namespace Inno.Core.Diagnose;

/// <summary>
/// Defines the presentation severity of a diagnostic.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// Describes useful information that does not indicate a problem.
    /// </summary>
    Info,

    /// <summary>
    /// Describes a recoverable issue that should be reviewed.
    /// </summary>
    Warning,

    /// <summary>
    /// Describes an issue that prevents the associated operation from succeeding.
    /// </summary>
    Error
}
