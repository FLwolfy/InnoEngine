using System;

namespace Inno.Core.Diagnostics;

/// <summary>
/// Represents one current issue reported by a compiler, importer, validator, or other diagnostic producer.
/// </summary>
public sealed class Diagnostic
{
    /// <summary>
    /// Creates a neutral diagnostic while keeping protocol, object and file identities distinct.
    /// </summary>
    /// <param name="code">
    /// The stable issue code within its producer.
    /// </param>
    /// <param name="message">
    /// The actionable human-readable message.
    /// </param>
    /// <param name="severity">
    /// The common engine impact classification.
    /// </param>
    /// <param name="semanticId">
    /// An optional extension or protocol identifier, never interpreted as a file path.
    /// </param>
    /// <param name="objectId">
    /// An optional persistent object identity, never a runtime handle.
    /// </param>
    /// <param name="location">
    /// An optional actual source file location.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The code is empty or an object identity is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// The message is null.
    /// </exception>
    public Diagnostic(string code, string message, DiagnosticSeverity severity,
        string? semanticId = null, Guid? objectId = null, DiagnosticLocation? location = null)
        : this(severity, code, message, location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (objectId == Guid.Empty)
            throw new ArgumentException("A diagnostic object identity cannot be empty.", nameof(objectId));
        this.semanticId = semanticId;
        this.objectId = objectId;
    }

    /// <summary>
    /// Gets the related protocol identifier without treating it as a source file.
    /// </summary>
    public string? semanticId { get; }

    /// <summary>
    /// Gets the related persistent object identity, or null for a non-object issue.
    /// </summary>
    public Guid? objectId { get; }

    /// <summary>
    /// Creates an informational diagnostic value without publishing it.
    /// </summary>
    /// <param name="code">
    /// The stable producer-defined code, or an empty string when no code is available.
    /// </param>
    /// <param name="message">
    /// The human-readable diagnostic message.
    /// </param>
    /// <param name="location">
    /// The related source location, or <see langword="null"/> when unavailable.
    /// </param>
    /// <returns>
    /// An immutable informational diagnostic that can be supplied to <see cref="Diagnostics.Set(string, Diagnostic)"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="message"/> is <see langword="null"/>.
    /// </exception>
    public static Diagnostic Info(
        string code,
        string message,
        DiagnosticLocation? location = null)
        => new(DiagnosticSeverity.Info, code, message, location);

    /// <summary>
    /// Creates a warning diagnostic value without publishing it.
    /// </summary>
    /// <param name="code">
    /// The stable producer-defined code, or an empty string when no code is available.
    /// </param>
    /// <param name="message">
    /// The human-readable diagnostic message.
    /// </param>
    /// <param name="location">
    /// The related source location, or <see langword="null"/> when unavailable.
    /// </param>
    /// <returns>
    /// An immutable warning diagnostic that can be supplied to <see cref="Diagnostics.Set(string, Diagnostic)"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="message"/> is <see langword="null"/>.
    /// </exception>
    public static Diagnostic Warning(
        string code,
        string message,
        DiagnosticLocation? location = null)
        => new(DiagnosticSeverity.Warning, code, message, location);

    /// <summary>
    /// Creates an error diagnostic value without publishing it.
    /// </summary>
    /// <param name="code">
    /// The stable producer-defined code, or an empty string when no code is available.
    /// </param>
    /// <param name="message">
    /// The human-readable diagnostic message.
    /// </param>
    /// <param name="location">
    /// The related source location, or <see langword="null"/> when unavailable.
    /// </param>
    /// <returns>
    /// An immutable error diagnostic that can be supplied to <see cref="Diagnostics.Set(string, Diagnostic)"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="message"/> is <see langword="null"/>.
    /// </exception>
    public static Diagnostic Error(
        string code,
        string message,
        DiagnosticLocation? location = null)
        => new(DiagnosticSeverity.Error, code, message, location);

    private Diagnostic(
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
