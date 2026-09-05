using System;

namespace Inno.Audio;

/// <summary>
/// Classifies the impact of one audio runtime diagnostic.
/// </summary>
public enum AudioDiagnosticSeverity
{
    /// <summary>
    /// Reports useful state without degraded behavior.
    /// </summary>
    Info,

    /// <summary>
    /// Reports degraded behavior that remains usable.
    /// </summary>
    Warning,

    /// <summary>
    /// Reports a failed audio operation or rejected candidate.
    /// </summary>
    Error
}

/// <summary>
/// Describes one structured audio runtime or backend diagnostic.
/// </summary>
public sealed record AudioDiagnostic
{
    /// <summary>
    /// Creates an immutable audio diagnostic.
    /// </summary>
    /// <param name="code">
    /// Stable machine-readable diagnostic code.
    /// </param>
    /// <param name="message">
    /// Human-readable diagnostic message.
    /// </param>
    /// <param name="severity">
    /// Diagnostic impact classification.
    /// </param>
    /// <param name="source">
    /// Optional extension, asset, or backend source identity.
    /// </param>
    public AudioDiagnostic(
        string code,
        string message,
        AudioDiagnosticSeverity severity,
        string? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        this.code = code;
        this.message = message;
        this.severity = severity;
        this.source = source;
    }

    /// <summary>
    /// Gets the stable machine-readable diagnostic code.
    /// </summary>
    public string code { get; }

    /// <summary>
    /// Gets the human-readable diagnostic message.
    /// </summary>
    public string message { get; }

    /// <summary>
    /// Gets the diagnostic impact classification.
    /// </summary>
    public AudioDiagnosticSeverity severity { get; }

    /// <summary>
    /// Gets the optional extension, asset, or backend source identity.
    /// </summary>
    public string? source { get; }
}

/// <summary>
/// Receives structured audio diagnostics without coupling the audio layer to a presentation system.
/// </summary>
public interface IAudioDiagnosticSink
{
    /// <summary>
    /// Publishes one current audio diagnostic.
    /// </summary>
    /// <param name="diagnostic">
    /// Immutable diagnostic to publish.
    /// </param>
    void Publish(AudioDiagnostic diagnostic);
}
