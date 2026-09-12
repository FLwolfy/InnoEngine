using Inno.Core.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Rendering;

/// <summary>
/// Maps a shader diagnostic back to an asset, pass, stage, and source position.
/// </summary>
public readonly record struct ShaderSourceLocation
{
    /// <summary>
    /// Creates a shader source location.
    /// </summary>
    /// <param name="assetPath">
    /// Project-relative source asset path.
    /// </param>
    /// <param name="passName">
    /// Stable pass name.
    /// </param>
    /// <param name="stage">
    /// Shader stage.
    /// </param>
    /// <param name="line">
    /// One-based source line, or zero when unavailable.
    /// </param>
    /// <param name="column">
    /// One-based source column, or zero when unavailable.
    /// </param>
    public ShaderSourceLocation(
        string assetPath,
        string passName,
        ShaderStage stage,
        int line = 0,
        int column = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(passName);
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        this.assetPath = assetPath;
        this.passName = passName;
        this.stage = stage;
        this.line = line;
        this.column = column;
    }

    /// <summary>
    /// Gets the project-relative source asset path.
    /// </summary>
    public string assetPath { get; }

    /// <summary>
    /// Gets the stable pass name.
    /// </summary>
    public string passName { get; }

    /// <summary>
    /// Gets the shader stage.
    /// </summary>
    public ShaderStage stage { get; }

    /// <summary>
    /// Gets the one-based line, or zero when unavailable.
    /// </summary>
    public int line { get; }

    /// <summary>
    /// Gets the one-based column, or zero when unavailable.
    /// </summary>
    public int column { get; }

}

/// <summary>
/// Reports one structured shader validation or compilation issue.
/// </summary>
public sealed class ShaderDiagnostic
{
    /// <summary>
    /// Creates a shader diagnostic.
    /// </summary>
    /// <param name="code">
    /// Stable diagnostic code.
    /// </param>
    /// <param name="severity">
    /// Diagnostic severity.
    /// </param>
    /// <param name="message">
    /// Artist-facing message.
    /// </param>
    /// <param name="location">
    /// Optional source mapping.
    /// </param>
    public ShaderDiagnostic(
        string code,
        DiagnosticSeverity severity,
        string message,
        ShaderSourceLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        this.code = code;
        this.severity = severity;
        this.message = message;
        this.location = location;
    }

    /// <summary>
    /// Gets the stable diagnostic code.
    /// </summary>
    public string code { get; }

    /// <summary>
    /// Gets the diagnostic severity.
    /// </summary>
    public DiagnosticSeverity severity { get; }

    /// <summary>
    /// Gets the artist-facing message.
    /// </summary>
    public string message { get; }

    /// <summary>
    /// Gets the optional source mapping.
    /// </summary>
    public ShaderSourceLocation? location { get; }
}
