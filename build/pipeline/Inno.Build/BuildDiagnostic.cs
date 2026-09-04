using System;

namespace Inno.Build;

/// <summary>
/// Identifies the severity of one structured build diagnostic.
/// </summary>
public enum BuildDiagnosticSeverity
{
    /// <summary>
    /// Provides non-failing build context.
    /// </summary>
    Information,

    /// <summary>
    /// Reports a condition that did not prevent output creation.
    /// </summary>
    Warning,

    /// <summary>
    /// Reports a condition that prevented a valid build.
    /// </summary>
    Error
}

/// <summary>
/// Describes one stable machine-readable build problem or informational event.
/// </summary>
public sealed class BuildDiagnostic
{
    /// <summary>
    /// Creates an immutable build diagnostic.
    /// </summary>
    /// <param name="severity">
    /// The impact of the diagnostic on build success.
    /// </param>
    /// <param name="code">
    /// The stable diagnostic code.
    /// </param>
    /// <param name="message">
    /// The human-readable explanation.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when code or message is empty.
    /// </exception>
    public BuildDiagnostic(BuildDiagnosticSeverity severity, string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        this.severity = severity;
        this.code = code;
        this.message = message;
    }

    /// <summary>
    /// Gets the impact of the diagnostic on build success.
    /// </summary>
    public BuildDiagnosticSeverity severity { get; }

    /// <summary>
    /// Gets the stable diagnostic code.
    /// </summary>
    public string code { get; }

    /// <summary>
    /// Gets the human-readable explanation.
    /// </summary>
    public string message { get; }
}
