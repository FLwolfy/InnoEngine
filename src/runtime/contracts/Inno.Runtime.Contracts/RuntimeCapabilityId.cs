using System;

namespace Inno.Runtime.Contracts;

/// <summary>
/// Identifies a backend-neutral capability supplied by composition, not an object or native handle.
/// </summary>
public readonly record struct RuntimeCapabilityId
{
    /// <summary>
    /// Creates an open capability protocol identifier.
    /// </summary>
    /// <param name="value">
    /// The non-empty, case-sensitive capability name.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The identifier is empty.
    /// </exception>
    public RuntimeCapabilityId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets the stable capability name, without interpreting it as a backend name.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this value contains a usable capability name.
    /// </summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats the capability for diagnostics.
    /// </summary>
    /// <returns>
    /// The protocol name, or an empty string for an uninitialized value.
    /// </returns>
    public override string ToString() => value ?? string.Empty;
}
