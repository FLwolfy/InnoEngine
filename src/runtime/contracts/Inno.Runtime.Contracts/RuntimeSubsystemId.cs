using System;

namespace Inno.Runtime.Contracts;

/// <summary>
/// Identifies one backend-neutral runtime subsystem protocol.
/// </summary>
public readonly record struct RuntimeSubsystemId
{
    /// <summary>
    /// Creates a stable runtime subsystem identifier.
    /// </summary>
    /// <param name="value">
    /// The non-empty protocol identifier.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is empty.
    /// </exception>
    public RuntimeSubsystemId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets the stable protocol value.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this identifier contains a usable protocol value.
    /// </summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats the subsystem identifier for diagnostics.
    /// </summary>
    /// <returns>
    /// The protocol value, or an empty string for an uninitialized value.
    /// </returns>
    public override string ToString() => value ?? string.Empty;
}
