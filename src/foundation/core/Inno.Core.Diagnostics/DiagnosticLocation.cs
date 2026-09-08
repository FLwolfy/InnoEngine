using System;

namespace Inno.Core.Diagnostics;

/// <summary>
/// Identifies an optional source location associated with a diagnostic.
/// </summary>
public readonly struct DiagnosticLocation : IEquatable<DiagnosticLocation>
{
    /// <summary>
    /// Creates a source location.
    /// </summary>
    /// <param name="sourcePath">
    /// The source path associated with the diagnostic.
    /// </param>
    /// <param name="line">
    /// The one-based line number, or zero when it is unavailable.
    /// </param>
    /// <param name="column">
    /// The one-based column number, or zero when it is unavailable.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="sourcePath"/> is empty or contains only whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="line"/> or <paramref name="column"/> is negative.
    /// </exception>
    public DiagnosticLocation(string sourcePath, int line = 0, int column = 0)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("A diagnostic source path is required.", nameof(sourcePath));
        if (line < 0)
            throw new ArgumentOutOfRangeException(nameof(line), "A diagnostic line cannot be negative.");
        if (column < 0)
            throw new ArgumentOutOfRangeException(nameof(column), "A diagnostic column cannot be negative.");
        this.sourcePath = sourcePath;
        this.line = line;
        this.column = column;
    }

    /// <summary>
    /// Gets the source path associated with the diagnostic.
    /// </summary>
    public string sourcePath { get; }

    /// <summary>
    /// Gets the one-based source line, or zero when it is unavailable.
    /// </summary>
    public int line { get; }

    /// <summary>
    /// Gets the one-based source column, or zero when it is unavailable.
    /// </summary>
    public int column { get; }

    /// <summary>
    /// Determines whether this instance and the supplied value represent the same logical state.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when both values represent the same logical state; otherwise, <see langword="false"/>.
    /// </returns>
    /// <param name="other">
    /// The value to compare with this instance.
    /// </param>
    public bool Equals(DiagnosticLocation other)
        => string.Equals(sourcePath, other.sourcePath, StringComparison.Ordinal) &&
           line == other.line &&
           column == other.column;

    /// <summary>
    /// Determines whether this instance and the supplied value represent the same logical state.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when both values represent the same logical state; otherwise, <see langword="false"/>.
    /// </returns>
    /// <param name="obj">
    /// The object to compare with this instance.
    /// </param>
    public override bool Equals(object? obj)
        => obj is DiagnosticLocation other && Equals(other);

    /// <summary>
    /// Computes a hash code from the fields that participate in logical equality.
    /// </summary>
    /// <returns>
    /// A hash code consistent with the implemented equality contract.
    /// </returns>
    public override int GetHashCode()
        => HashCode.Combine(sourcePath, line, column);

    /// <summary>
    /// Determines whether two locations are equal.
    /// </summary>
    /// <param name="left">
    /// The left location.
    /// </param>
    /// <param name="right">
    /// The right location.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both locations identify the same source position.
    /// </returns>
    public static bool operator ==(DiagnosticLocation left, DiagnosticLocation right)
        => left.Equals(right);

    /// <summary>
    /// Determines whether two locations are different.
    /// </summary>
    /// <param name="left">
    /// The left location.
    /// </param>
    /// <param name="right">
    /// The right location.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the locations identify different source positions.
    /// </returns>
    public static bool operator !=(DiagnosticLocation left, DiagnosticLocation right)
        => !left.Equals(right);
}
