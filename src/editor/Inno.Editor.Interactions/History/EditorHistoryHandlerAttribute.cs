using System;

namespace Inno.Editor.Interactions;

/// <summary>
/// Registers a stateless editor history handler for one stable change protocol.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EditorHistoryHandlerAttribute : Attribute
{
    /// <summary>
    /// Creates a history handler registration.
    /// </summary>
    /// <param name="kind">The stable globally unique change protocol identifier.</param>
    /// <param name="version">The positive current payload schema version produced by this handler generation.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="kind"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is not positive.</exception>
    public EditorHistoryHandlerAttribute(string kind, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version), version, "History handler versions must be positive.");
        this.kind = kind;
        this.version = version;
    }

    /// <summary>
    /// Gets the stable globally unique change protocol identifier.
    /// </summary>
    public string kind { get; }

    /// <summary>
    /// Gets the current payload schema version produced by this handler generation.
    /// </summary>
    public int version { get; }
}
