using System;

namespace Inno.Core.Diagnostics;

/// <summary>
/// Represents one current issue reported by a compiler, importer, validator, or other diagnostic producer.
/// </summary>
public sealed class Diagnostic
{
    /// <summary>
    /// Creates a diagnostic without a source location.
    /// </summary>
    /// <param name="severity">The diagnostic severity.</param>
    /// <param name="code">The stable producer-defined code, or an empty string when no code is available.</param>
    /// <param name="message">The human-readable diagnostic message.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is <see langword="null"/>.</exception>
    public Diagnostic(DiagnosticSeverity severity, string code, string message)
        : this(severity, code, message, location: null)
    {
    }

    /// <summary>
    /// Creates a diagnostic with an optional source location.
    /// </summary>
    /// <param name="severity">The diagnostic severity.</param>
    /// <param name="code">The stable producer-defined code, or an empty string when no code is available.</param>
    /// <param name="message">The human-readable diagnostic message.</param>
    /// <param name="location">The related source location, or <see langword="null"/> when unavailable.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is <see langword="null"/>.</exception>
    public Diagnostic(
        DiagnosticSeverity severity,
        string code,
        string message,
        DiagnosticLocation? location)
    {
        ArgumentNullException.ThrowIfNull(message);
        this.severity = severity;
        this.code = code ?? string.Empty;
        this.message = message;
        this.location = location;
    }

    /// <summary>
    /// Gets the diagnostic severity.
    /// </summary>
    public DiagnosticSeverity severity { get; }

    /// <summary>
    /// Gets the stable producer-defined code, or an empty string when unavailable.
    /// </summary>
    public string code { get; }

    /// <summary>
    /// Gets the human-readable diagnostic message.
    /// </summary>
    public string message { get; }

    /// <summary>
    /// Gets the related source location, or <see langword="null"/> when unavailable.
    /// </summary>
    public DiagnosticLocation? location { get; }
}
